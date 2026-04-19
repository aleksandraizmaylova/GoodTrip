namespace TourismApp.Models
{
    public class Attraction
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ShortDescription { get; set; }
        public string? FullDescription { get; set; }
        public int CategoryId { get; set; }
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string[]? ImageUrls { get; set; }
    }

    public class AttractionCategory
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? IconUrl { get; set; }
        public string? Color { get; set; }
    }

    public class UserAttractionStatus
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int AttractionId { get; set; }
        public string Status { get; set; } = "not_visited";
        public DateTime UpdatedAt { get; set; }
    }

    public class Review
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int AttractionId { get; set; }
        public int Rating { get; set; }
        public string? Text { get; set; }
        public string[]? Photos { get; set; }
    }

    public class Achievement
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? IconUrl { get; set; }
        public string? CriteriaType { get; set; }
        public int? CriteriaValue { get; set; }
        public string? BadgeColor { get; set; }
    }

    public class UserAchievement
    {
        public int UserId { get; set; }
        public int AchievementId { get; set; }
        public DateTime EarnedAt { get; set; }
    }
}