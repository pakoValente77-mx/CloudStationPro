using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
    [Authorize(AuthenticationSchemes = "Identity.Application," + JwtBearerDefaults.AuthenticationScheme)]
    public class ChatHub : Hub
    {
        private readonly IConfiguration _config;
        private readonly ILogger<ChatHub> _logger;
        private readonly string _sqlConn;
        private readonly Services.PushNotificationService _pushService;
        private readonly Services.CentinelaBotService _centinelaBot;

        // Track online users: connectionId → { userId, userName, fullName }
        private static readonly Dictionary<string, OnlineUser> _onlineUsers = new();

        // Track which rooms each user is in: userName → Set of rooms
        private static readonly Dictionary<string, HashSet<string>> _userRooms = new();

        public ChatHub(IConfiguration config, ILogger<ChatHub> logger, Services.PushNotificationService pushService, Services.CentinelaBotService centinelaBot)
        {
            _config = config;
            _logger = logger;
            _sqlConn = config.GetConnectionString("SqlServer")!;
            _pushService = pushService;
            _centinelaBot = centinelaBot;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier ?? Context.User?.Identity?.Name ?? "anonymous";
            var userName = Context.User?.Identity?.Name ?? "anonymous";
            var fullName = Context.User?.FindFirst("fullName")?.Value ?? userName;

            // Detect platform: desktop app sends ?platform=desktop
            var platform = Context.GetHttpContext()?.Request.Query["platform"].FirstOrDefault() ?? "web";

            _onlineUsers[Context.ConnectionId] = new OnlineUser
            {
                UserId = userId,
                UserName = userName,
                FullName = fullName,
                ConnectedAt = DateTime.UtcNow,
                Platform = platform
            };

            // Auto-join "general" room
            await Groups.AddToGroupAsync(Context.ConnectionId, "general");

            // Auto-join "centinela" bot room
            await Groups.AddToGroupAsync(Context.ConnectionId, Services.CentinelaBotService.BotRoom);

            // Auto-join personal DM group so private messages reach this user
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userName}");

            await Clients.All.SendAsync("UserConnected", new
            {
                userId,
                userName,
                platform,
                onlineCount = _onlineUsers.Count
            });

            _logger.LogInformation("Chat: {User} connected ({ConnId})", userName, Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _onlineUsers.Remove(Context.ConnectionId, out var user);

            // Clean up room tracking
            if (user != null && !string.IsNullOrEmpty(user.UserName))
            {
                // Only remove userRooms if this was the last connection for the user
                var stillConnected = _onlineUsers.Values.Any(u => u.UserName == user.UserName);
                if (!stillConnected)
                {
                    _userRooms.Remove(user.UserName);
                }
            }

            await Clients.All.SendAsync("UserDisconnected", new
            {
                userId = user?.UserId,
                userName = user?.UserName,
                platform = user?.Platform,
                onlineCount = _onlineUsers.Count
            });

            _logger.LogInformation("Chat: {User} disconnected", user?.UserName ?? "unknown");
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Send a message to a room (group or DM). Persists to DB and broadcasts.
        /// DM rooms use format "dm:{userA}:{userB}" (sorted alphabetically).
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
                    INSERT INTO ChatMessages (Id, ChatId, Room, UserId, UserName, FullName, Message, Timestamp,
                                              FileName, FileUrl, FileSize, FileType)
                    VALUES (@Id, @ChatId, @Room, @UserId, @UserName, @FullName, @Message, @Timestamp,
                            @FileName, @FileUrl, @FileSize, @FileType)", msg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving chat message");
            }

            // For DM rooms, send to both user personal groups instead of a shared group
            if (room.StartsWith("dm:"))
            {
                var parts = room.Split(':');
                if (parts.Length == 3)
                {
                    // Send to both participants via their personal groups
                    await Clients.Group($"user:{parts[1]}").SendAsync("ReceiveMessage", msg);
                    await Clients.Group($"user:{parts[2]}").SendAsync("ReceiveMessage", msg);

                    // Push to offline DM participants
                    await SendPushToOfflineUsers(new[] { parts[1], parts[2] }, msg);
                    return;
                }
            }

            // Broadcast to room (group chats)
            await Clients.Group(msg.Room).SendAsync("ReceiveMessage", msg);

            // Push to users who are members of this room but not currently online
            await SendPushToOfflineRoomMembers(msg);

            // Centinela bot: respond if message is in the centinela room or mentions @Centinela
            await TriggerCentinelaIfNeededAsync(room, message, user?.UserName ?? "anonymous");
        }

        /// <summary>
        /// Check if Centinela bot should respond and dispatch its reply.
        /// Triggers on: room == "centinela" OR message contains @Centinela.
        /// </summary>
        private async Task TriggerCentinelaIfNeededAsync(string room, string message, string userName)
        {
            // Don't respond to our own messages
            if (userName == Services.CentinelaBotService.BotUserName) return;

            bool isBotRoom = string.Equals(room, Services.CentinelaBotService.BotRoom, StringComparison.OrdinalIgnoreCase);
            bool isMentioned = Services.CentinelaBotService.IsMentioned(message);

            if (!isBotRoom && !isMentioned) return;

            // Strip @mention if present
            var query = isMentioned ? Services.CentinelaBotService.StripMention(message) : message;
            if (string.IsNullOrWhiteSpace(query)) query = "/ayuda";

            try
            {
                var botResult = await _centinelaBot.ProcessMessageAsync(query, userName);

                var botMsg = new ChatMessage
                {
                    Id = Guid.NewGuid(),
                    ChatId = Guid.NewGuid(),
                    Room = room,
                    UserId = Services.CentinelaBotService.BotUserId,
                    UserName = Services.CentinelaBotService.BotUserName,
                    FullName = Services.CentinelaBotService.BotFullName,
                    Message = botResult.Message,
                    Timestamp = DateTime.UtcNow,
                    FileName = botResult.FileName,
                    FileUrl = botResult.FileUrl,
                    FileSize = botResult.FileSize,
                    FileType = botResult.FileType
                };

                // Persist bot message
                try
                {
                    using var db = new SqlConnection(_sqlConn);
                    await db.ExecuteAsync(@"
                        INSERT INTO ChatMessages (Id, ChatId, Room, UserId, UserName, FullName, Message, Timestamp,
                                                  FileName, FileUrl, FileSize, FileType)
                        VALUES (@Id, @ChatId, @Room, @UserId, @UserName, @FullName, @Message, @Timestamp,
                                @FileName, @FileUrl, @FileSize, @FileType)", botMsg);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving Centinela bot message");
                }

                // Broadcast bot response to the same room
                await Clients.Group(room).SendAsync("ReceiveMessage", botMsg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Centinela bot response");
            }
        }

        /// <summary>
        /// Send push notifications to users who are offline (not connected via SignalR)
        /// </summary>
        private async Task SendPushToOfflineUsers(IEnumerable<string> targetUserNames, ChatMessage msg)
        {
            if (!_pushService.IsConfigured) return;

            var onlineUserNames = _onlineUsers.Values.Select(u => u.UserName).Distinct().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var senderName = msg.UserName;

            foreach (var userName in targetUserNames)
            {
                // Don't push to the sender or to online users
                if (string.Equals(userName, senderName, StringComparison.OrdinalIgnoreCase)) continue;
                if (onlineUserNames.Contains(userName)) continue;

                try
                {
                    // Look up userId from UserName
                    using var db = new SqlConnection(_sqlConn);
                    var userId = await db.QueryFirstOrDefaultAsync<string>(
                        "SELECT Id FROM AspNetUsers WHERE UserName = @UserName",
                        new { UserName = userName });

                    if (!string.IsNullOrEmpty(userId))
                    {
                        var data = new Dictionary<string, string>
                        {
                            ["room"] = msg.Room,
                            ["sender"] = msg.UserName,
                            ["type"] = "chat_message"
                        };
                        await _pushService.SendToUserAsync(userId, msg.FullName ?? msg.UserName, msg.Message, data);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending push to user {User}", userName);
                }
            }
        }

        /// <summary>
        /// Send push to members of a room who are not online. 
        /// For group rooms, we query recent participants.
        /// </summary>
        private async Task SendPushToOfflineRoomMembers(ChatMessage msg)
        {
            if (!_pushService.IsConfigured) return;

            try
            {
                // Get distinct users who have sent messages in this room (recent participants)
                using var db = new SqlConnection(_sqlConn);
                var recentParticipants = (await db.QueryAsync<string>(@"
                    SELECT DISTINCT UserName FROM ChatMessages 
                    WHERE Room = @Room AND Timestamp > DATEADD(DAY, -7, GETUTCDATE())",
                    new { Room = msg.Room })).ToList();

                await SendPushToOfflineUsers(recentParticipants, msg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending push to room members for room {Room}", msg.Room);
            }
        }

        /// <summary>
        /// Get the canonical DM room name between two users (sorted alphabetically).
        /// </summary>
        public static string GetDmRoom(string userA, string userB)
        {
            var sorted = new[] { userA, userB }.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            return $"dm:{sorted[0]}:{sorted[1]}";
        }

        /// <summary>
        /// Join a specific chat room
        /// </summary>
        public async Task JoinRoom(string room)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, room);

            var userName = _onlineUsers.GetValueOrDefault(Context.ConnectionId)?.UserName;
            if (!string.IsNullOrEmpty(userName))
            {
                if (!_userRooms.ContainsKey(userName))
                    _userRooms[userName] = new HashSet<string>();
                _userRooms[userName].Add(room);
            }

            await Clients.Group(room).SendAsync("SystemMessage", new
            {
                message = $"{userName} se unió a {room}",
                timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Leave a chat room
        /// </summary>
        public async Task LeaveRoom(string room)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, room);

            var userName = _onlineUsers.GetValueOrDefault(Context.ConnectionId)?.UserName;
            if (!string.IsNullOrEmpty(userName) && _userRooms.ContainsKey(userName))
            {
                _userRooms[userName].Remove(room);
            }
        }

        /// <summary>
        /// Get list of online users (called from SignalR clients)
        /// </summary>
        public Task<List<object>> GetOnlineUsers()
        {
            return Task.FromResult(GetOnlineUsersStatic());
        }

        /// <summary>
        /// Static accessor for online users — used by ChatController HTTP endpoint
        /// </summary>
        public static List<object> GetOnlineUsersStatic()
        {
            var users = _onlineUsers.Values
                .GroupBy(u => u.UserName)
                .Select(g => {
                    var first = g.First();
                    var platforms = g.Select(u => u.Platform).Distinct().ToList();
                    return (object)new { first.UserId, first.UserName, first.FullName, platforms };
                })
                .ToList();
            return users;
        }

        private class OnlineUser
        {
            public string UserId { get; set; } = "";
            public string UserName { get; set; } = "";
            public string FullName { get; set; } = "";
            public DateTime ConnectedAt { get; set; }
            public string Platform { get; set; } = "web";
        }
    }
}
