using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CloudStationWeb.Services
{
    /// <summary>
    /// Push notification service using Firebase Cloud Messaging (FCM).
    /// Supports sending to individual devices, topics, and managing device tokens.
    /// Used by both web (alerts) and mobile app (chat + alerts).
    /// </summary>
    public class PushNotificationService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<PushNotificationService> _logger;
        private readonly string _sqlConn;
        private bool _initialized;

        public PushNotificationService(IConfiguration config, ILogger<PushNotificationService> logger)
        {
            _config = config;
            _logger = logger;
            _sqlConn = config.GetConnectionString("SqlServer")!;

            InitializeFirebase();
        }

        private void InitializeFirebase()
        {
            var credPath = _config["Firebase:CredentialsPath"];
            if (string.IsNullOrEmpty(credPath) || !File.Exists(credPath))
            {
                _logger.LogWarning("Firebase credentials not found at '{Path}'. Push notifications disabled.", credPath);
                _initialized = false;
                return;
            }

            try
            {
                if (FirebaseApp.DefaultInstance == null)
                {
                    FirebaseApp.Create(new AppOptions
                    {
                        Credential = GoogleCredential.FromFile(credPath)
                    });
                }
                _initialized = true;
                _logger.LogInformation("Firebase initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Firebase");
                _initialized = false;
            }
        }

        public bool IsConfigured => _initialized;

        /// <summary>
        /// Send push to a specific device token
        /// </summary>
        public async Task<string?> SendToDeviceAsync(string token, string title, string body, Dictionary<string, string>? data = null)
        {
            if (!_initialized) return null;

            var message = new Message
            {
                Token = token,
                Notification = new Notification { Title = title, Body = body },
                Data = data,
                Android = new AndroidConfig
                {
                    Priority = Priority.High,
                    Notification = new AndroidNotification { Sound = "default", ChannelId = "alertas" }
                },
                Apns = new ApnsConfig
                {
                    Aps = new Aps { Sound = "default", ContentAvailable = true }
                }
            };

            try
            {
                return await FirebaseMessaging.DefaultInstance.SendAsync(message);
            }
            catch (FirebaseMessagingException ex)
            {
                _logger.LogError(ex, "FCM send to device failed: {Token}", token);
                return null;
            }
        }

        /// <summary>
        /// Send push to a topic (e.g., "alertas", "chat")
        /// </summary>
        public async Task<string?> SendToTopicAsync(string topic, string title, string body, Dictionary<string, string>? data = null)
        {
            if (!_initialized) return null;

            var message = new Message
            {
                Topic = topic,
                Notification = new Notification { Title = title, Body = body },
                Data = data,
                Android = new AndroidConfig
                {
                    Priority = Priority.High,
                    Notification = new AndroidNotification { Sound = "default", ChannelId = topic }
                },
                Apns = new ApnsConfig
                {
                    Aps = new Aps { Sound = "default", ContentAvailable = true }
                }
            };

            try
            {
                var response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                _logger.LogInformation("FCM sent to topic '{Topic}': {Response}", topic, response);
                return response;
            }
            catch (FirebaseMessagingException ex)
            {
                _logger.LogError(ex, "FCM send to topic '{Topic}' failed", topic);
                return null;
            }
        }

        /// <summary>
        /// Register a device token for a user (called from mobile app)
        /// </summary>
        public async Task RegisterDeviceAsync(string userId, string token, string platform)
        {
            using var db = new SqlConnection(_sqlConn);
            // Upsert: if token exists for user, update; otherwise insert
            await db.ExecuteAsync(@"
                IF EXISTS (SELECT 1 FROM DeviceTokens WHERE UserId = @UserId AND Token = @Token)
                    UPDATE DeviceTokens SET Platform = @Platform, LastSeen = GETUTCDATE() WHERE UserId = @UserId AND Token = @Token
                ELSE
                    INSERT INTO DeviceTokens (UserId, Token, Platform, CreatedAt, LastSeen) 
                    VALUES (@UserId, @Token, @Platform, GETUTCDATE(), GETUTCDATE())",
                new { UserId = userId, Token = token, Platform = platform });
        }

        /// <summary>
        /// Remove a device token (on logout)
        /// </summary>
        public async Task UnregisterDeviceAsync(string userId, string token)
        {
            using var db = new SqlConnection(_sqlConn);
            await db.ExecuteAsync("DELETE FROM DeviceTokens WHERE UserId = @UserId AND Token = @Token",
                new { UserId = userId, Token = token });
        }

        /// <summary>
        /// Send push to all devices of a specific user
        /// </summary>
        public async Task SendToUserAsync(string userId, string title, string body, Dictionary<string, string>? data = null)
        {
            if (!_initialized) return;

            using var db = new SqlConnection(_sqlConn);
            var tokens = (await db.QueryAsync<string>(
                "SELECT Token FROM DeviceTokens WHERE UserId = @UserId AND LastSeen > DATEADD(DAY, -30, GETUTCDATE())",
                new { UserId = userId })).ToList();

            foreach (var token in tokens)
            {
                await SendToDeviceAsync(token, title, body, data);
            }
        }
    }
}
