using System.ComponentModel.DataAnnotations;

namespace CloudStationWeb.Models
{
    public class LoginAudit
    {
        [Key]
        public long Id { get; set; }
        public string? UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool Success { get; set; }
        public string? FailureReason { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? Provider { get; set; } // "Local", "Google", "Microsoft", "2FA"
    }
}
