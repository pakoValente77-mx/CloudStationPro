using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CloudStationWeb.Services
{
    /// <summary>
    /// Esquema de autenticación nativo de ASP.NET Core para X-Api-Key.
    /// Permite usar [Authorize(AuthenticationSchemes = "ApiKey")] de forma declarativa.
    /// </summary>
    public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
    {
        public const string SchemeName = "ApiKey";
        public const string HeaderName = "X-Api-Key";
    }

    public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
    {
        private readonly IConfiguration _config;

        public ApiKeyAuthenticationHandler(
            IOptionsMonitor<ApiKeyAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IConfiguration config)
            : base(options, logger, encoder)
        {
            _config = config;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Leer la clave del header
            if (!Request.Headers.TryGetValue(ApiKeyAuthenticationOptions.HeaderName, out var keyValues))
                return Task.FromResult(AuthenticateResult.NoResult()); // No hay header: dejar que otro esquema lo intente

            var providedKey = keyValues.ToString();
            var configuredKey = _config["ImageStore:ApiKey"] ?? "";

            if (string.IsNullOrEmpty(configuredKey) || !string.Equals(providedKey, configuredKey, StringComparison.Ordinal))
            {
                Logger.LogWarning("[ApiKey] Intento de acceso con clave inválida desde {IP}",
                    Context.Connection.RemoteIpAddress);
                return Task.FromResult(AuthenticateResult.Fail("API Key inválida"));
            }

            // Construir identidad autenticada con rol ApiConsumer
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "api-consumer"),
                new Claim(ClaimTypes.Role, "ApiConsumer"),
                new Claim(ClaimTypes.AuthenticationMethod, "ApiKey")
            };
            var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationOptions.SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationOptions.SchemeName);

            Logger.LogDebug("[ApiKey] Acceso autenticado via X-Api-Key desde {IP}",
                Context.Connection.RemoteIpAddress);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = 401;
            Response.ContentType = "application/json";
            return Response.WriteAsync("{\"error\":\"Se requiere autenticación: X-Api-Key header o JWT Bearer token.\"}");
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = 403;
            Response.ContentType = "application/json";
            return Response.WriteAsync("{\"error\":\"Acceso denegado. Rol insuficiente.\"}");
        }
    }
}
