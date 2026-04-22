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

app.MapGet("/", () => Results.Redirect("/v3.html"));

app.MapGet("/api/sights/search", async (HttpRequest request, string query, decimal? lat, decimal? lng, int? radius) =>
{
    var currentUser = await GetCurrentUserAsync(connectionString, request);
    if (currentUser is null)
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(query))
    {
        return Results.BadRequest(new { error = "Необходим поисковый запрос" });
    }

    var searchRadius = radius ?? 1000;
    var attractions = await GetAttractionsAsync(connectionString, query.Trim());

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
        requestedBy = new { id = currentUser.Id, username = currentUser.Username, email = currentUser.Email },
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
                            SELECT id, username, email, password_hash
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
            email
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
        user = new { id = currentUser.Id, username = currentUser.Username, email = currentUser.Email }
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

app.MapPost("api/achievements", async (HttpRequest request) => // это заглушка, хочу отправлять юзера и получать кучу всего (см achievements.js)
{
    return Results.Ok(new
    {
        success = true,
        achievements = new[] { }
    });
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

static async Task<List<ExternalAttraction>> GetAttractionsAsync(string connectionString, string query)
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
                           COALESCE(AVG(r.rating), 0) AS rating
                       FROM attractions a
                       LEFT JOIN attraction_categories c ON c.id = a.category_id
                       LEFT JOIN reviews r ON r.attraction_id = a.id
                       WHERE
                           a.name ILIKE @searchPattern OR
                           COALESCE(a.short_description, '') ILIKE @searchPattern OR
                           COALESCE(a.full_description, '') ILIKE @searchPattern OR
                           COALESCE(c.name, '') ILIKE @searchPattern
                       GROUP BY a.id, a.name, description, a.latitude, a.longitude, address, category
                       ORDER BY a.id;
                       """;

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("searchPattern", $"%{query}%");

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
            reader.GetDecimal(7)));
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
                                   COALESCE(AVG(r.rating), 0) AS rating
                               FROM attractions a
                               LEFT JOIN attraction_categories c ON c.id = a.category_id
                               LEFT JOIN reviews r ON r.attraction_id = a.id
                               GROUP BY a.id, a.name, description, a.latitude, a.longitude, address, category
                               ORDER BY a.id;
                               """;

    await using var fallbackCommand = new NpgsqlCommand(fallbackSql, connection);
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
            fallbackReader.GetDecimal(7)));
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
                       SELECT u.id, u.username, u.email
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

    return new CurrentUser(reader.GetInt32(0), reader.GetString(1), reader.GetString(2));
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

    var sqlScript = await File.ReadAllTextAsync(scriptPath);
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(sqlScript, connection);
    await command.ExecuteNonQueryAsync();
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
public record CurrentUser(int Id, string Username, string Email);