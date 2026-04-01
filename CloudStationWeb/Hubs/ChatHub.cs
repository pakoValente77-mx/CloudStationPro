using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using CloudStationWeb.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CloudStationWeb.Hubs
{
    /// <summary>
    /// SignalR hub for real-time chat. Supports rooms, message persistence,
    /// and is accessible from both web (cookie auth) and mobile (JWT auth).
    /// </summary>
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly IConfiguration _config;
        private readonly ILogger<ChatHub> _logger;
        private readonly string _sqlConn;

        // Track online users: connectionId → { userId, userName, fullName }
        private static readonly Dictionary<string, OnlineUser> _onlineUsers = new();

        public ChatHub(IConfiguration config, ILogger<ChatHub> logger)
        {
            _config = config;
            _logger = logger;
            _sqlConn = config.GetConnectionString("SqlServer")!;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier ?? Context.User?.Identity?.Name ?? "anonymous";
            var userName = Context.User?.Identity?.Name ?? "anonymous";

            _onlineUsers[Context.ConnectionId] = new OnlineUser
            {
                UserId = userId,
                UserName = userName,
                ConnectedAt = DateTime.UtcNow
            };

            // Auto-join "general" room
            await Groups.AddToGroupAsync(Context.ConnectionId, "general");

            await Clients.All.SendAsync("UserConnected", new
            {
                userId,
                userName,
                onlineCount = _onlineUsers.Count
            });

            _logger.LogInformation("Chat: {User} connected ({ConnId})", userName, Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _onlineUsers.Remove(Context.ConnectionId, out var user);

            await Clients.All.SendAsync("UserDisconnected", new
            {
                userId = user?.UserId,
                userName = user?.UserName,
                onlineCount = _onlineUsers.Count
            });

            _logger.LogInformation("Chat: {User} disconnected", user?.UserName ?? "unknown");
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Send a message to a room. Persists to DB and broadcasts.
        /// </summary>
        public async Task SendMessage(string room, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            var user = _onlineUsers.GetValueOrDefault(Context.ConnectionId);
            var msg = new ChatMessage
            {
                Id = Guid.NewGuid(),
                ChatId = Guid.NewGuid(),
                Room = room ?? "general",
                UserId = user?.UserId ?? Context.UserIdentifier ?? "anonymous",
                UserName = user?.UserName ?? "anonymous",
                FullName = user?.FullName ?? user?.UserName ?? "anonymous",
                Message = message,
                Timestamp = DateTime.UtcNow
            };

            // Persist to SQL Server
            try
            {
                using var db = new SqlConnection(_sqlConn);
                await db.ExecuteAsync(@"
                    INSERT INTO ChatMessages (Id, ChatId, Room, UserId, UserName, FullName, Message, Timestamp)
                    VALUES (@Id, @ChatId, @Room, @UserId, @UserName, @FullName, @Message, @Timestamp)", msg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving chat message");
            }

            // Broadcast to room
            await Clients.Group(msg.Room).SendAsync("ReceiveMessage", msg);
        }

        /// <summary>
        /// Join a specific chat room
        /// </summary>
        public async Task JoinRoom(string room)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, room);
            await Clients.Group(room).SendAsync("SystemMessage", new
            {
                message = $"{_onlineUsers.GetValueOrDefault(Context.ConnectionId)?.UserName} se unió a {room}",
                timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Leave a chat room
        /// </summary>
        public async Task LeaveRoom(string room)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, room);
        }

        /// <summary>
        /// Get list of online users
        /// </summary>
        public Task<List<object>> GetOnlineUsers()
        {
            var users = _onlineUsers.Values
                .Select(u => (object)new { u.UserId, u.UserName, u.FullName })
                .Distinct()
                .ToList();
            return Task.FromResult(users);
        }

        private class OnlineUser
        {
            public string UserId { get; set; } = "";
            public string UserName { get; set; } = "";
            public string FullName { get; set; } = "";
            public DateTime ConnectedAt { get; set; }
        }
    }
}
