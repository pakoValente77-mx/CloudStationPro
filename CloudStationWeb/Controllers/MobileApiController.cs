using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CloudStationWeb.Services;

namespace CloudStationWeb.Controllers
{
    /// <summary>
    /// API endpoints for mobile app. Uses JWT Bearer authentication.
    /// Provides device registration, push notification management,
    /// and chat history for mobile clients.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class MobileApiController : ControllerBase
    {
        private readonly PushNotificationService _pushService;
        private readonly IConfiguration _config;

        public MobileApiController(PushNotificationService pushService, IConfiguration config)
        {
            _pushService = pushService;
            _config = config;
        }

        /// <summary>
        /// Register device for push notifications (called on app start/login)
        /// POST /api/MobileApi/RegisterDevice
        /// </summary>
        [HttpPost("RegisterDevice")]
        public async Task<IActionResult> RegisterDevice([FromBody] DeviceRegistration request)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            await _pushService.RegisterDeviceAsync(userId, request.Token, request.Platform);
            return Ok(new { success = true });
        }

        /// <summary>
        /// Unregister device (called on logout)
        /// POST /api/MobileApi/UnregisterDevice
        /// </summary>
        [HttpPost("UnregisterDevice")]
        public async Task<IActionResult> UnregisterDevice([FromBody] DeviceRegistration request)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            await _pushService.UnregisterDeviceAsync(userId, request.Token);
            return Ok(new { success = true });
        }

        /// <summary>
        /// Check push notification status
        /// GET /api/MobileApi/PushStatus
        /// </summary>
        [HttpGet("PushStatus")]
        public IActionResult PushStatus()
        {
            return Ok(new
            {
                firebaseConfigured = _pushService.IsConfigured,
                signalREndpoint = "/hubs/chat",
                topics = new[] { "alertas", "chat", "operacion" }
            });
        }
    }

    public class DeviceRegistration
    {
        public string Token { get; set; } = string.Empty;
        public string Platform { get; set; } = "android"; // "android" | "ios" | "web"
    }
}
