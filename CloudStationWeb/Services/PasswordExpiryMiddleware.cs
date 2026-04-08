using Microsoft.AspNetCore.Identity;
using CloudStationWeb.Models;

namespace CloudStationWeb.Services
{
    public class PasswordExpiryMiddleware
    {
        private readonly RequestDelegate _next;
        private const int PasswordMaxAgeDays = 30;

        private static readonly string[] ExcludedPrefixes = new[]
        {
            "/Account/ForceChangePassword",
            "/Account/Logout",
            "/Account/Login",
            "/api/",
            "/hubs/",
            "/css/",
            "/js/",
            "/lib/",
            "/images/",
            "/favicon"
        };

        public PasswordExpiryMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var path = context.Request.Path.Value ?? "";

                // Skip excluded paths (static files, API, login/logout, the change page itself)
                if (!ExcludedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                    var user = await userManager.GetUserAsync(context.User);

                    if (user != null)
                    {
                        var lastChanged = user.PasswordLastChanged ?? user.CreatedAt;
                        var age = DateTime.UtcNow - lastChanged;

                        if (age.TotalDays >= PasswordMaxAgeDays)
                        {
                            context.Response.Redirect("/Account/ForceChangePassword");
                            return;
                        }
                    }
                }
            }

            await _next(context);
        }
    }
}
