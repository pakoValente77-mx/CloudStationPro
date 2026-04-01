namespace CloudStationWeb.Models
{
    public class ChatMessage
    {
        public Guid Id { get; set; }
        public Guid ChatId { get; set; }
        public string Room { get; set; } = "general";
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public class ChatRoom
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class SendChatMessageRequest
    {
        public string Room { get; set; } = "general";
        public string Message { get; set; } = string.Empty;
    }

    public class ChatHistoryRequest
    {
        public string Room { get; set; } = "general";
        public DateTime? Since { get; set; }
        public int Limit { get; set; } = 50;
    }
}
