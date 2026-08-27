namespace PrivacyEventProcessing.Domain.Models;
    public class EventRequest
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

