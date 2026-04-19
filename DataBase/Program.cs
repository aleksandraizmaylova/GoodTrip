using TourismApp.Models;
using Npgsql;

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

await EnsureDatabaseInitializedAsync(connectionString, app.Environment.ContentRootPath);

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/sights/search", async (string query, decimal? lat, decimal? lng, int? radius) =>
{
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
        count = filtered.Count,
        data = filtered
    });
});

app.Run();

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