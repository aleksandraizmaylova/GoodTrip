namespace TourismApp.Models;

public record Coordinates(decimal Lat, decimal Lng);

public record ExternalAttraction(
    string Id,
    string Name,
    string Description,
    decimal Latitude,
    decimal Longitude,
    string Address,
    string Category,
    decimal Rating)
{
    public Coordinates Coordinates { get; } = new(Latitude, Longitude);
}
