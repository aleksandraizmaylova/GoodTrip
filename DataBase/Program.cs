using TourismApp.Models;
using Npgsql;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=tourism_app;Username=postgres;Password=postgres";

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowAll");
app.UseDefaultFiles();
app.UseStaticFiles();

await EnsureDatabaseInitializedAsync(connectionString, app.Environment.ContentRootPath);

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/", () => Results.Redirect("/map.html"));

app.MapGet("/api/sights/search", async (HttpRequest request, string? query, decimal? lat, decimal? lng, int? radius) =>
{
    var currentUser = await GetCurrentUserAsync(connectionString, request);

    var searchRadius = radius ?? 1000;
    var normalizedQuery = string.IsNullOrWhiteSpace(query) ? string.Empty : query.Trim();
    var attractions = await GetAttractionsAsync(connectionString, normalizedQuery, currentUser?.Id);

    var filtered = attractions;

    if (lat.HasValue && lng.HasValue)
    {
        filtered = filtered
            .Where(a => GeoDistanceMeters(lat.Value, lng.Value, a.Coordinates.Lat, a.Coordinates.Lng) <= searchRadius)
            .ToList();
    }

    return Results.Ok(new
    {
        success = true,
        requestedBy = currentUser is null
            ? null
            : new { id = currentUser.Id, username = currentUser.Username, email = currentUser.Email },
        count = filtered.Count,
        data = filtered
    });
});

app.MapPost("/api/register", async (HttpRequest request) =>
{
    var payload = await ReadAuthPayloadAsync(request);

    if (string.IsNullOrWhiteSpace(payload.Username) ||
        string.IsNullOrWhiteSpace(payload.Email) ||
        string.IsNullOrWhiteSpace(payload.Password))
    {
        return Results.BadRequest(new { success = false, error = "username, email и password обязательны" });
    }

    var normalizedEmail = payload.Email.Trim().ToLowerInvariant();
    var passwordHash = BCrypt.Net.BCrypt.HashPassword(payload.Password);

    const string registerSql = """
                               INSERT INTO users (username, email, password_hash)
                               VALUES (@username, @email, @passwordHash)
                               RETURNING id;
                               """;

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(registerSql, connection);
    command.Parameters.AddWithValue("username", payload.Username.Trim());
    command.Parameters.AddWithValue("email", normalizedEmail);
    command.Parameters.AddWithValue("passwordHash", passwordHash);

    try
    {
        var userId = (int)(await command.ExecuteScalarAsync() ?? 0);
        return Results.Ok(new { success = true, userId });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        return Results.Conflict(new { success = false, error = "Пользователь с таким email или username уже существует" });
    }
});

app.MapPost("/api/login", async (HttpRequest request) =>
{
    var payload = await ReadAuthPayloadAsync(request);

    if (string.IsNullOrWhiteSpace(payload.Email) || string.IsNullOrWhiteSpace(payload.Password))
    {
        return Results.BadRequest(new { success = false, error = "email и password обязательны" });
    }

    const string loginSql = """
                            SELECT id, username, email, password_hash, COALESCE(is_admin, FALSE)
                            FROM users
                            WHERE email = @email
                            LIMIT 1;
                            """;

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(loginSql, connection);
    command.Parameters.AddWithValue("email", payload.Email.Trim().ToLowerInvariant());

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return Results.Unauthorized();
    }

    var passwordHash = reader.GetString(3);
    if (!BCrypt.Net.BCrypt.Verify(payload.Password, passwordHash))
    {
        return Results.Unauthorized();
    }

    var userId = reader.GetInt32(0);
    var username = reader.GetString(1);
    var email = reader.GetString(2);
    var isAdmin = reader.GetBoolean(4);

    var sessionToken = await CreateSessionAsync(connectionString, userId);
    request.HttpContext.Response.Cookies.Append("auth_token", sessionToken, new CookieOptions
    {
        HttpOnly = true,
        Secure = false,
        SameSite = SameSiteMode.Lax,
        Expires = DateTimeOffset.UtcNow.AddDays(7)
    });

    return Results.Ok(new
    {
        success = true,
        token = sessionToken,
        user = new
        {
            id = userId,
            username,
            email,
            isAdmin
        }
    });
});

app.MapGet("/api/me", async (HttpRequest request) =>
{
    var currentUser = await GetCurrentUserAsync(connectionString, request);
    if (currentUser is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new
    {
        success = true,
        user = new { id = currentUser.Id, username = currentUser.Username, email = currentUser.Email, isAdmin = currentUser.IsAdmin }
    });
});

app.MapPost("/api/logout", async (HttpRequest request) =>
{
    var token = GetTokenFromRequest(request);
    if (!string.IsNullOrWhiteSpace(token))
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM user_sessions WHERE token = @token;", connection);
        command.Parameters.AddWithValue("token", token);
        await command.ExecuteNonQueryAsync();
    }

    request.HttpContext.Response.Cookies.Delete("auth_token");
    return Results.Ok(new { success = true });
});

app.MapPost("/api/achievements", async (HttpRequest request) =>
{
    var currentUser = await GetCurrentUserAsync(connectionString, request);
    if (currentUser is null)
    {
        return Results.Unauthorized();
    }

    var achievements = await GetAchievementsPayloadAsync(connectionString, currentUser.Id);
    return Results.Ok(new
    {
        success = true,
        user = new { id = currentUser.Id, username = currentUser.Username, email = currentUser.Email },
        achievements
    });
});

app.MapPost("/api/propose-attraction", async (HttpRequest request) =>
{
    var currentUser = await GetCurrentUserAsync(connectionString, request);
    if (currentUser is null) return Results.Unauthorized();

    const string sql = """
                       INSERT INTO user_achievements (user_id, achievement_id, earned_at)
                       SELECT @userId, a.id, NOW()
                       FROM achievements a
                       WHERE a.code = 'achievement13'
                       ON CONFLICT DO NOTHING;
                       """;

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("userId", currentUser.Id);
    await command.ExecuteNonQueryAsync();

    return Results.Ok(new { success = true });
});

app.MapGet("/api/user/attraction-status", async (HttpRequest request) =>
{
    var currentUser = await GetCurrentUserAsync(connectionString, request);
    if (currentUser is null) return Results.Unauthorized();

    const string sql = """
                       SELECT attraction_id, status
                       FROM user_attraction_status
                       WHERE user_id = @userId;
                       """;

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("userId", currentUser.Id);

    var planned = new List<int>();
    var visited = new List<int>();

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var attractionId = reader.GetInt32(0);
        var status = reader.GetString(1);
        if (status == "planned") planned.Add(attractionId);
        if (status == "visited") visited.Add(attractionId);
    }

    return Results.Ok(new { success = true, planned, visited });
});

app.MapPost("/api/attractions/{attractionId:int}/status", async (HttpRequest request, int attractionId) =>
{
    var currentUser = await GetCurrentUserAsync(connectionString, request);
    if (currentUser is null) return Results.Unauthorized();

    var payload = await request.ReadFromJsonAsync<AttractionStatusPayload>();
    var status = payload?.Status?.Trim();
    if (string.IsNullOrWhiteSpace(status) || status is not ("not_visited" or "planned" or "visited"))
    {
        return Results.BadRequest(new { success = false, error = "status must be one of: not_visited, planned, visited" });
    }

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    if (status == "not_visited")
    {
        await using var deleteCmd = new NpgsqlCommand(
            "DELETE FROM user_attraction_status WHERE user_id = @userId AND attraction_id = @attractionId;",
            connection);
        deleteCmd.Parameters.AddWithValue("userId", currentUser.Id);
        deleteCmd.Parameters.AddWithValue("attractionId", attractionId);
        await deleteCmd.ExecuteNonQueryAsync();
        return Results.Ok(new { success = true, status });
    }

    const string upsertSql = """
                             INSERT INTO user_attraction_status (user_id, attraction_id, status, updated_at)
                             VALUES (@userId, @attractionId, @status, NOW())
                             ON CONFLICT (user_id, attraction_id)
                             DO UPDATE SET status = EXCLUDED.status, updated_at = NOW();
                             """;

    await using var upsertCmd = new NpgsqlCommand(upsertSql, connection);
    upsertCmd.Parameters.AddWithValue("userId", currentUser.Id);
    upsertCmd.Parameters.AddWithValue("attractionId", attractionId);
    upsertCmd.Parameters.AddWithValue("status", status);
    await upsertCmd.ExecuteNonQueryAsync();

    return Results.Ok(new { success = true, status });
});

app.MapPost("/api/admin/attractions", async (HttpRequest request, CreateAttractionPayload? payload) =>
{
    var adminCheck = await RequireAdminAsync(connectionString, request);
    if (adminCheck.Error is not null)
    {
        return adminCheck.Error;
    }

    if (payload is null)
    {
        return Results.BadRequest(new { success = false, error = "Тело запроса пустое" });
    }

    var name = payload.Name?.Trim();
    if (string.IsNullOrWhiteSpace(name))
    {
        return Results.BadRequest(new { success = false, error = "name обязателен" });
    }

    if (payload.Latitude is < -90 or > 90)
    {
        return Results.BadRequest(new { success = false, error = "latitude должен быть в диапазоне [-90, 90]" });
    }

    if (payload.Longitude is < -180 or > 180)
    {
        return Results.BadRequest(new { success = false, error = "longitude должен быть в диапазоне [-180, 180]" });
    }

    var imageUrls = payload.ImageUrls?
        .Where(url => !string.IsNullOrWhiteSpace(url))
        .Select(url => url.Trim())
        .ToArray() ?? Array.Empty<string>();

    const string insertSql = """
                             INSERT INTO attractions (
                                 name,
                                 short_description,
                                 full_description,
                                 category_id,
                                 latitude,
                                 longitude,
                                 address,
                                 city,
                                 image_urls
                             )
                             VALUES (
                                 @name,
                                 @shortDescription,
                                 @fullDescription,
                                 @categoryId,
                                 @latitude,
                                 @longitude,
                                 @address,
                                 @city,
                                 @imageUrls
                             )
                             RETURNING id;
                             """;

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(insertSql, connection);
    command.Parameters.AddWithValue("name", name);
    command.Parameters.AddWithValue("shortDescription", (object?)payload.ShortDescription?.Trim() ?? DBNull.Value);
    command.Parameters.AddWithValue("fullDescription", (object?)payload.FullDescription?.Trim() ?? DBNull.Value);
    command.Parameters.AddWithValue("categoryId", payload.CategoryId is > 0 ? payload.CategoryId.Value : DBNull.Value);
    command.Parameters.AddWithValue("latitude", payload.Latitude);
    command.Parameters.AddWithValue("longitude", payload.Longitude);
    command.Parameters.AddWithValue("address", (object?)payload.Address?.Trim() ?? DBNull.Value);
    command.Parameters.AddWithValue("city", (object?)payload.City?.Trim() ?? DBNull.Value);
    command.Parameters.AddWithValue("imageUrls", imageUrls.Length > 0 ? imageUrls : DBNull.Value);

    try
    {
        var newId = (int)(await command.ExecuteScalarAsync() ?? 0);
        return Results.Ok(new { success = true, id = newId });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
    {
        return Results.BadRequest(new { success = false, error = "Неверный category_id: такой категории не существует" });
    }
});

app.MapGet("/api/admin/categories", async (HttpRequest request) =>
{
    var adminCheck = await RequireAdminAsync(connectionString, request);
    if (adminCheck.Error is not null)
    {
        return adminCheck.Error;
    }

    const string sql = """
                       SELECT id, name, COALESCE(icon_url, '') AS icon_url, COALESCE(color, '') AS color
                       FROM attraction_categories
                       ORDER BY id;
                       """;

    var rows = new List<object>();
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new
        {
            id = reader.GetInt32(0),
            name = reader.GetString(1),
            iconUrl = reader.GetString(2),
            color = reader.GetString(3)
        });
    }

    return Results.Ok(new { success = true, data = rows });
});

app.MapPost("/api/admin/categories", async (HttpRequest request, UpsertCategoryPayload? payload) =>
{
    var adminCheck = await RequireAdminAsync(connectionString, request);
    if (adminCheck.Error is not null)
    {
        return adminCheck.Error;
    }

    var name = payload?.Name?.Trim();
    if (string.IsNullOrWhiteSpace(name))
    {
        return Results.BadRequest(new { success = false, error = "name обязателен" });
    }

    const string sql = """
                       INSERT INTO attraction_categories (name, icon_url, color)
                       VALUES (@name, @iconUrl, @color)
                       RETURNING id;
                       """;

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("name", name);
    command.Parameters.AddWithValue("iconUrl", (object?)payload?.IconUrl?.Trim() ?? DBNull.Value);
    command.Parameters.AddWithValue("color", (object?)payload?.Color?.Trim() ?? DBNull.Value);

    try
    {
        var id = (int)(await command.ExecuteScalarAsync() ?? 0);
        return Results.Ok(new { success = true, id });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        return Results.Conflict(new { success = false, error = "Категория с таким названием уже существует" });
    }
});

app.MapPut("/api/admin/categories/{id:int}", async (HttpRequest request, int id, UpsertCategoryPayload? payload) =>
{
    var adminCheck = await RequireAdminAsync(connectionString, request);
    if (adminCheck.Error is not null)
    {
        return adminCheck.Error;
    }

    var name = payload?.Name?.Trim();
    if (string.IsNullOrWhiteSpace(name))
    {
        return Results.BadRequest(new { success = false, error = "name обязателен" });
    }

    const string sql = """
                       UPDATE attraction_categories
                       SET name = @name,
                           icon_url = @iconUrl,
                           color = @color
                       WHERE id = @id;
                       """;

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("name", name);
    command.Parameters.AddWithValue("iconUrl", (object?)payload?.IconUrl?.Trim() ?? DBNull.Value);
    command.Parameters.AddWithValue("color", (object?)payload?.Color?.Trim() ?? DBNull.Value);

    try
    {
        var updated = await command.ExecuteNonQueryAsync();
        if (updated == 0)
        {
            return Results.NotFound(new { success = false, error = "Категория не найдена" });
        }
        return Results.Ok(new { success = true });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        return Results.Conflict(new { success = false, error = "Категория с таким названием уже существует" });
    }
});

app.MapDelete("/api/admin/categories/{id:int}", async (HttpRequest request, int id) =>
{
    var adminCheck = await RequireAdminAsync(connectionString, request);
    if (adminCheck.Error is not null)
    {
        return adminCheck.Error;
    }

    const string sql = "DELETE FROM attraction_categories WHERE id = @id;";
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("id", id);

    try
    {
        var deleted = await command.ExecuteNonQueryAsync();
        if (deleted == 0)
        {
            return Results.NotFound(new { success = false, error = "Категория не найдена" });
        }
        return Results.Ok(new { success = true });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
    {
        return Results.BadRequest(new { success = false, error = "Нельзя удалить категорию: она используется в местах" });
    }
});

app.MapGet("/api/admin/attractions", async (HttpRequest request) =>
{
    var adminCheck = await RequireAdminAsync(connectionString, request);
    if (adminCheck.Error is not null)
    {
        return adminCheck.Error;
    }

    const string sql = """
                       SELECT
                           a.id,
                           a.name,
                           COALESCE(a.short_description, '') AS short_description,
                           COALESCE(a.full_description, '') AS full_description,
                           a.category_id,
                           COALESCE(c.name, '') AS category_name,
                           a.latitude,
                           a.longitude,
                           COALESCE(a.address, '') AS address,
                           COALESCE(a.city, '') AS city,
                           COALESCE(a.image_urls, ARRAY[]::TEXT[]) AS image_urls
                       FROM attractions a
                       LEFT JOIN attraction_categories c ON c.id = a.category_id
                       ORDER BY a.id;
                       """;

    var rows = new List<object>();
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var imageUrls = reader.GetFieldValue<string[]>(10);
        rows.Add(new
        {
            id = reader.GetInt32(0),
            name = reader.GetString(1),
            shortDescription = reader.GetString(2),
            fullDescription = reader.GetString(3),
            categoryId = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
            categoryName = reader.GetString(5),
            latitude = reader.GetDecimal(6),
            longitude = reader.GetDecimal(7),
            address = reader.GetString(8),
            city = reader.GetString(9),
            imageUrls
        });
    }

    return Results.Ok(new { success = true, data = rows });
});

app.MapPut("/api/admin/attractions/{id:int}", async (HttpRequest request, int id, CreateAttractionPayload? payload) =>
{
    var adminCheck = await RequireAdminAsync(connectionString, request);
    if (adminCheck.Error is not null)
    {
        return adminCheck.Error;
    }

    if (payload is null)
    {
        return Results.BadRequest(new { success = false, error = "Тело запроса пустое" });
    }

    var name = payload.Name?.Trim();
    if (string.IsNullOrWhiteSpace(name))
    {
        return Results.BadRequest(new { success = false, error = "name обязателен" });
    }

    if (payload.Latitude is < -90 or > 90)
    {
        return Results.BadRequest(new { success = false, error = "latitude должен быть в диапазоне [-90, 90]" });
    }

    if (payload.Longitude is < -180 or > 180)
    {
        return Results.BadRequest(new { success = false, error = "longitude должен быть в диапазоне [-180, 180]" });
    }

    var imageUrls = payload.ImageUrls?
        .Where(url => !string.IsNullOrWhiteSpace(url))
        .Select(url => url.Trim())
        .ToArray() ?? Array.Empty<string>();

    const string sql = """
                       UPDATE attractions
                       SET name = @name,
                           short_description = @shortDescription,
                           full_description = @fullDescription,
                           category_id = @categoryId,
                           latitude = @latitude,
                           longitude = @longitude,
                           address = @address,
                           city = @city,
                           image_urls = @imageUrls
                       WHERE id = @id;
                       """;

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("name", name);
    command.Parameters.AddWithValue("shortDescription", (object?)payload.ShortDescription?.Trim() ?? DBNull.Value);
    command.Parameters.AddWithValue("fullDescription", (object?)payload.FullDescription?.Trim() ?? DBNull.Value);
    command.Parameters.AddWithValue("categoryId", payload.CategoryId is > 0 ? payload.CategoryId.Value : DBNull.Value);
    command.Parameters.AddWithValue("latitude", payload.Latitude);
    command.Parameters.AddWithValue("longitude", payload.Longitude);
    command.Parameters.AddWithValue("address", (object?)payload.Address?.Trim() ?? DBNull.Value);
    command.Parameters.AddWithValue("city", (object?)payload.City?.Trim() ?? DBNull.Value);
    command.Parameters.AddWithValue("imageUrls", imageUrls.Length > 0 ? imageUrls : DBNull.Value);

    try
    {
        var updated = await command.ExecuteNonQueryAsync();
        if (updated == 0)
        {
            return Results.NotFound(new { success = false, error = "Достопримечательность не найдена" });
        }
        return Results.Ok(new { success = true });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
    {
        return Results.BadRequest(new { success = false, error = "Неверный category_id: такой категории не существует" });
    }
});

app.MapDelete("/api/admin/attractions/{id:int}", async (HttpRequest request, int id) =>
{
    var adminCheck = await RequireAdminAsync(connectionString, request);
    if (adminCheck.Error is not null)
    {
        return adminCheck.Error;
    }

    const string sql = "DELETE FROM attractions WHERE id = @id;";
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("id", id);
    var deleted = await command.ExecuteNonQueryAsync();
    if (deleted == 0)
    {
        return Results.NotFound(new { success = false, error = "Достопримечательность не найдена" });
    }
    return Results.Ok(new { success = true });
});

app.Run();

static async Task<AuthPayload> ReadAuthPayloadAsync(HttpRequest request)
{
    if (request.HasFormContentType)
    {
        var form = await request.ReadFormAsync();
        return new AuthPayload(
            form["username"].ToString(),
            form["email"].ToString(),
            form["password"].ToString());
    }

    var payload = await request.ReadFromJsonAsync<AuthPayload>();
    return payload ?? new AuthPayload(string.Empty, string.Empty, string.Empty);
}

static async Task<List<ExternalAttraction>> GetAttractionsAsync(string connectionString, string query, int? userId = null)
{
    var attractions = new List<ExternalAttraction>();
    const string sql = """
                       SELECT
                           a.id,
                           a.name,
                           COALESCE(a.short_description, a.full_description, '') AS description,
                           a.latitude,
                           a.longitude,
                           COALESCE(a.address, '') AS address,
                           COALESCE(c.name, 'Без категории') AS category,
                           COALESCE(AVG(r.rating), 0) AS rating,
                           COALESCE(uas.status, 'not_visited') AS status
                       FROM attractions a
                       LEFT JOIN attraction_categories c ON c.id = a.category_id
                       LEFT JOIN reviews r ON r.attraction_id = a.id
                       LEFT JOIN user_attraction_status uas ON uas.attraction_id = a.id AND uas.user_id = @userId
                       WHERE
                           a.name ILIKE @searchPattern OR
                           COALESCE(a.short_description, '') ILIKE @searchPattern OR
                           COALESCE(a.full_description, '') ILIKE @searchPattern OR
                           COALESCE(c.name, '') ILIKE @searchPattern
                       GROUP BY a.id, a.name, description, a.latitude, a.longitude, address, category, uas.status
                       ORDER BY a.id;
                       """;

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("searchPattern", $"%{query}%");
    command.Parameters.AddWithValue("userId", userId ?? (object)DBNull.Value);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        attractions.Add(new ExternalAttraction(
            reader.GetInt32(0).ToString(),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetDecimal(3),
            reader.GetDecimal(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetDecimal(7),
            reader.GetString(8)));
    }

    if (attractions.Count > 0)
    {
        return attractions;
    }

    // If strict search returned nothing, fallback to all attractions.
    const string fallbackSql = """
                               SELECT
                                   a.id,
                                   a.name,
                                   COALESCE(a.short_description, a.full_description, '') AS description,
                                   a.latitude,
                                   a.longitude,
                                   COALESCE(a.address, '') AS address,
                                   COALESCE(c.name, 'Без категории') AS category,
                                   COALESCE(AVG(r.rating), 0) AS rating,
                                   COALESCE(uas.status, 'not_visited') AS status
                               FROM attractions a
                               LEFT JOIN attraction_categories c ON c.id = a.category_id
                               LEFT JOIN reviews r ON r.attraction_id = a.id
                               LEFT JOIN user_attraction_status uas ON uas.attraction_id = a.id AND uas.user_id = @userId
                               GROUP BY a.id, a.name, description, a.latitude, a.longitude, address, category, uas.status
                               ORDER BY a.id;
                               """;

    await using var fallbackCommand = new NpgsqlCommand(fallbackSql, connection);
    fallbackCommand.Parameters.AddWithValue("userId", userId ?? (object)DBNull.Value);
    await using var fallbackReader = await fallbackCommand.ExecuteReaderAsync();
    while (await fallbackReader.ReadAsync())
    {
        attractions.Add(new ExternalAttraction(
            fallbackReader.GetInt32(0).ToString(),
            fallbackReader.GetString(1),
            fallbackReader.GetString(2),
            fallbackReader.GetDecimal(3),
            fallbackReader.GetDecimal(4),
            fallbackReader.GetString(5),
            fallbackReader.GetString(6),
            fallbackReader.GetDecimal(7),
            fallbackReader.GetString(8)));
    }

    return attractions;
}

static async Task<string> CreateSessionAsync(string connectionString, int userId)
{
    var tokenBytes = RandomNumberGenerator.GetBytes(32);
    var token = Convert.ToBase64String(tokenBytes);

    const string sql = """
                       INSERT INTO user_sessions (user_id, token, expires_at)
                       VALUES (@userId, @token, @expiresAt);
                       """;

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("userId", userId);
    command.Parameters.AddWithValue("token", token);
    command.Parameters.AddWithValue("expiresAt", DateTime.UtcNow.AddDays(7));
    await command.ExecuteNonQueryAsync();

    return token;
}

static async Task<CurrentUser?> GetCurrentUserAsync(string connectionString, HttpRequest request)
{
    var token = GetTokenFromRequest(request);
    if (string.IsNullOrWhiteSpace(token))
    {
        return null;
    }

    const string sql = """
                       SELECT u.id, u.username, u.email, COALESCE(u.is_admin, FALSE)
                       FROM user_sessions s
                       JOIN users u ON u.id = s.user_id
                       WHERE s.token = @token AND s.expires_at > NOW()
                       LIMIT 1;
                       """;

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("token", token);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    return new CurrentUser(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3));
}

static async Task<(CurrentUser? User, IResult? Error)> RequireAdminAsync(string connectionString, HttpRequest request)
{
    var currentUser = await GetCurrentUserAsync(connectionString, request);
    if (currentUser is null)
    {
        return (null, Results.Json(
            new { success = false, error = "Требуется авторизация" },
            statusCode: StatusCodes.Status401Unauthorized));
    }

    if (!currentUser.IsAdmin)
    {
        return (null, Results.Json(
            new { success = false, error = "Доступ только для администратора" },
            statusCode: StatusCodes.Status403Forbidden));
    }

    return (currentUser, null);
}

static string? GetTokenFromRequest(HttpRequest request)
{
    if (request.Cookies.TryGetValue("auth_token", out var cookieToken) && !string.IsNullOrWhiteSpace(cookieToken))
    {
        return cookieToken;
    }

    var authHeader = request.Headers.Authorization.ToString();
    if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return authHeader["Bearer ".Length..].Trim();
    }

    return null;
}

static async Task EnsureDatabaseInitializedAsync(string connectionString, string contentRootPath)
{
    var scriptPath = Path.Combine(contentRootPath, "DB_structure.sql");
    if (!File.Exists(scriptPath))
    {
        throw new FileNotFoundException("Database initialization script was not found.", scriptPath);
    }

    // Connect with backoff to avoid restart-loops on VM when Postgres isn't ready yet.
    // Fail fast on wrong credentials (28P01), because retries won't help.
    await using var connection = new NpgsqlConnection(connectionString);
    var delayMs = 500;
    while (true)
    {
        try
        {
            await connection.OpenAsync();
            break;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidPassword)
        {
            throw;
        }
        catch
        {
            await Task.Delay(delayMs);
            delayMs = Math.Min(delayMs * 2, 10_000);
        }
    }

    // IMPORTANT: Do not execute the full DB_structure.sql on every API start.
    // On hosting/restarts this can cause heavy locks/CPU load and make Postgres appear "down".
    // Use an advisory lock so multiple API instances don't initialize concurrently.
    const long initLockKey = 912345678901234567; // arbitrary constant, stable across deployments

    await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(@k);", connection))
    {
        lockCmd.Parameters.AddWithValue("k", initLockKey);
        await lockCmd.ExecuteNonQueryAsync();
    }

    try
    {
        const string schemaExistsSql = """
                                       SELECT EXISTS (
                                         SELECT 1
                                         FROM information_schema.tables
                                         WHERE table_schema = 'public' AND table_name = 'users'
                                       );
                                       """;

        await using var existsCmd = new NpgsqlCommand(schemaExistsSql, connection);
        var existsObj = await existsCmd.ExecuteScalarAsync();
        var schemaExists = existsObj is bool b && b;

        if (!schemaExists)
        {
            var sqlScript = await File.ReadAllTextAsync(scriptPath);
            await using var command = new NpgsqlCommand(sqlScript, connection);
            await command.ExecuteNonQueryAsync();
        }

        const string adminColumnSql = """
                                      ALTER TABLE users
                                      ADD COLUMN IF NOT EXISTS is_admin BOOLEAN NOT NULL DEFAULT FALSE;
                                      """;
        await using var adminColumnCommand = new NpgsqlCommand(adminColumnSql, connection);
        await adminColumnCommand.ExecuteNonQueryAsync();
    }
    finally
    {
        await using var unlockCmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@k);", connection);
        unlockCmd.Parameters.AddWithValue("k", initLockKey);
        await unlockCmd.ExecuteNonQueryAsync();
    }
}

static async Task<Dictionary<string, object>> GetAchievementsPayloadAsync(string connectionString, int userId)
{
    const string sql = """
                       WITH totals AS (
                         SELECT COUNT(*)::decimal AS total_users
                         FROM users
                       ),
                       achieved AS (
                         SELECT a.id AS achievement_id, a.code, COUNT(ua.user_id)::decimal AS achieved_users
                         FROM achievements a
                         LEFT JOIN user_achievements ua ON ua.achievement_id = a.id
                         GROUP BY a.id, a.code
                       ),
                       me AS (
                         SELECT a.code, ua.earned_at
                         FROM achievements a
                         LEFT JOIN user_achievements ua
                           ON ua.achievement_id = a.id AND ua.user_id = @userId
                       )
                       SELECT
                         a.code,
                         a.name,
                         a.description,
                         COALESCE(a.icon_url, '') AS img,
                         m.earned_at,
                         CASE
                           WHEN t.total_users = 0 THEN 0
                           ELSE ROUND((ac.achieved_users / t.total_users) * 100.0, 2)
                         END AS percent
                       FROM achievements a
                       JOIN achieved ac ON ac.achievement_id = a.id
                       CROSS JOIN totals t
                       JOIN me m ON m.code = a.code
                       WHERE a.code IS NOT NULL
                       ORDER BY a.id;
                       """;

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("userId", userId);

    var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var code = reader.GetString(0);
        var name = reader.GetString(1);
        var description = reader.GetString(2);
        var img = reader.GetString(3);
        var earnedAt = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4);
        var percent = reader.GetDecimal(5);

        result[code] = new
        {
            id = code,
            name,
            description,
            img,
            get = earnedAt.HasValue ? earnedAt.Value.ToString("yyyy-MM-dd") : "Не получено",
            percent = percent.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    return result;
}

static double GeoDistanceMeters(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
{
    const double earthRadiusKm = 6371;

    var startLat = (double)lat1 * Math.PI / 180;
    var endLat = (double)lat2 * Math.PI / 180;
    var deltaLat = ((double)lat2 - (double)lat1) * Math.PI / 180;
    var deltaLon = ((double)lon2 - (double)lon1) * Math.PI / 180;

    var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
            Math.Cos(startLat) * Math.Cos(endLat) *
            Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);
    var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

    return earthRadiusKm * c * 1000;
}

public record AuthPayload(string Username, string Email, string Password);
public record CurrentUser(int Id, string Username, string Email, bool IsAdmin);
public record AttractionStatusPayload(string Status);
public record CreateAttractionPayload(
    string Name,
    string? ShortDescription,
    string? FullDescription,
    int? CategoryId,
    decimal Latitude,
    decimal Longitude,
    string? Address,
    string? City,
    string[]? ImageUrls);
public record UpsertCategoryPayload(
    string Name,
    string? IconUrl,
    string? Color);