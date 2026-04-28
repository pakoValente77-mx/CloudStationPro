using OfficeOpenXml;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using CloudStationWeb.Data;
using CloudStationWeb.Models;

var builder = WebApplication.CreateBuilder(args);

// Allow large file uploads (200 MB)
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 200_000_000; // 200 MB
});

// Set EPPlus 8 license
ExcelPackage.License.SetNonCommercialPersonal("CloudStation User");

// Add EF Core with SQL Server for Identity
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer"),
        sqlOptions => sqlOptions.UseCompatibilityLevel(110)));

// Add ASP.NET Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Password policy
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;

    // Lockout settings
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    // User settings
    options.User.RequireUniqueEmail = false; // Allow users without email (local accounts)
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// ─── Duración de sesión desde configuración (CVE-M4) ────────────────────────
var sessionExpireHours = builder.Configuration.GetValue<int>("Security:Session:ExpireHours", 8);
var slidingExpiration = builder.Configuration.GetValue<bool>("Security:Session:SlidingExpiration", false);

// Configure cookie authentication (CVE-A1, CVE-M4)
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(sessionExpireHours);
    options.SlidingExpiration = slidingExpiration;
    options.Cookie.HttpOnly = true;
    // FIX CVE-A1: forzar siempre Secure (solo HTTPS) en lugar de SameAsRequest
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    // FIX CVE-A1: SameSite Strict para mitigar CSRF
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Name = "__pih_session";
    // When an API call arrives without cookie, return 401 instead of redirect
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = 401;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

// Configure JWT Bearer authentication for API consumers (mobile, desktop, etc.)
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"] ?? throw new InvalidOperationException("Jwt:Key is missing in configuration");
builder.Services.AddAuthentication()
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        };

        // Allow SignalR to receive JWT from query string (WebSocket can't send headers)
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            // Suppress JWT 401 challenge for browser requests so cookie auth can redirect to login
            OnChallenge = context =>
            {
                if (!context.Request.Path.StartsWithSegments("/api"))
                {
                    context.HandleResponse();
                }
                return Task.CompletedTask;
            }
        };
    });

// Configure external authentication (Google & Microsoft)
var googleSection = builder.Configuration.GetSection("Authentication:Google");
if (!string.IsNullOrEmpty(googleSection["ClientId"]))
{
    builder.Services.AddAuthentication()
        .AddGoogle(options =>
        {
            options.ClientId = googleSection["ClientId"]!;
            options.ClientSecret = googleSection["ClientSecret"]!;
        });
}

var msSection = builder.Configuration.GetSection("Authentication:Microsoft");
if (!string.IsNullOrEmpty(msSection["ClientId"]))
{
    builder.Services.AddAuthentication()
        .AddMicrosoftAccount(options =>
        {
            options.ClientId = msSection["ClientId"]!;
            options.ClientSecret = msSection["ClientSecret"]!;
        });
}

// Add services to the container
builder.Services.AddHttpClient();
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddScoped<CloudStationWeb.Services.DataService>();
builder.Services.AddScoped<CloudStationWeb.Services.FunVasosService>();
builder.Services.AddScoped<CloudStationWeb.Services.BhgService>();
builder.Services.AddScoped<CloudStationWeb.Services.HydroForecastService>();
builder.Services.AddScoped<CloudStationWeb.Services.IEmailSender, CloudStationWeb.Services.SmtpEmailSender>();
builder.Services.AddSingleton<CloudStationWeb.Services.PushNotificationService>();
builder.Services.AddSingleton<CloudStationWeb.Services.ChartService>();
builder.Services.AddSingleton<CloudStationWeb.Services.CentinelaBotService>();
builder.Services.AddSingleton<CloudStationWeb.Services.EarlyWarningService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CloudStationWeb.Services.EarlyWarningService>());
builder.Services.AddSingleton<CloudStationWeb.Services.PrecipitationAlertService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CloudStationWeb.Services.PrecipitationAlertService>());

// ─── CORS — FIX CVE-C3: restringir orígenes a lista configurada ──────────────
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    // Política estricta para la web (solo orígenes conocidos)
    options.AddPolicy("ApiCors", policy =>
    {
        if (allowedOrigins.Length > 0)
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        else
            // Sin orígenes configurados: bloquear todo cross-origin
            policy.SetIsOriginAllowed(_ => false);
    });

    // Política abierta solo para WebSockets de SignalR (móvil/desktop nativo)
    options.AddPolicy("SignalRCors", policy =>
        policy.WithOrigins(allowedOrigins.Length > 0 ? allowedOrigins : new[] { "null" })
              .AllowAnyHeader()
              .AllowCredentials());
});

// ─── Rate Limiting — FIX CVE-A4 ──────────────────────────────────────────────
builder.Services.AddRateLimiter(rl =>
{
    // Límite de login: 10 intentos por minuto por IP
    rl.AddFixedWindowLimiter("login", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
        opt.AutoReplenishment = true;
    });

    // Límite de registro: 5 por minuto por IP
    rl.AddFixedWindowLimiter("register", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
        opt.AutoReplenishment = true;
    });

    // Límite de API REST: 120 peticiones por minuto por IP
    rl.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit = 120;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 5;
        opt.AutoReplenishment = true;
    });

    // Respuesta 429 cuando se excede el límite
    rl.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"error\":\"Demasiadas solicitudes. Intente más tarde.\"}",
            cancellationToken: token);
    };
});

var app = builder.Build();

// Apply pending migrations and seed data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();
        await SeedData.InitializeAsync(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error during database migration/seeding.");
    }
}

// ─── Pipeline de seguridad ───────────────────────────────────────────────────

// Cloudflare / IIS reverse proxy: respetar headers X-Forwarded-*
// FIX CVE-A1: KnownNetworks solo acepta proxies de red local para evitar IP spoofing
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
    // Solo confiar en loopback y red privada (ajustar si el proxy está en otra subred)
    KnownNetworks =
    {
        new Microsoft.AspNetCore.HttpOverrides.IPNetwork(
            System.Net.IPAddress.Parse("10.0.0.0"), 8),
        new Microsoft.AspNetCore.HttpOverrides.IPNetwork(
            System.Net.IPAddress.Parse("172.16.0.0"), 12),
        new Microsoft.AspNetCore.HttpOverrides.IPNetwork(
            System.Net.IPAddress.Parse("192.168.0.0"), 16)
    }
});

// FIX CVE-A1: forzar HTTPS en todas las peticiones
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // HSTS: forzar HTTPS por 1 año en navegadores
    app.UseHsts();
}

// Redirigir HTTP → HTTPS
app.UseHttpsRedirection();

// FIX CVE-A2: Headers de seguridad HTTP
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    // Prevenir MIME sniffing
    headers.Append("X-Content-Type-Options", "nosniff");
    // Prevenir clickjacking
    headers.Append("X-Frame-Options", "DENY");
    // Referrer mínimo (no filtrar a terceros)
    headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    // Deshabilitar funciones de navegador innecesarias
    headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=()");
    // Content Security Policy básico (scripts solo del mismo origen)
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        headers.Append("Content-Security-Policy",
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net https://unpkg.com https://cdnjs.cloudflare.com https://maps.googleapis.com; " +
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; " +
            "font-src 'self' https://fonts.gstatic.com https://cdnjs.cloudflare.com data:; " +
            "img-src 'self' data: blob: https:; " +
            "connect-src 'self' wss: ws:; " +
            "frame-ancestors 'none';");
    }
    await next();
});

var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".kml"] = "application/vnd.google-earth.kml+xml";
provider.Mappings[".kmz"] = "application/vnd.google-earth.kmz";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider
});

app.UseRouting();

// Rate limiting (antes de auth para proteger endpoints)
app.UseRateLimiter();

app.UseCors("ApiCors");
app.UseAuthentication();
app.UseAuthorization();

// FIX CVE-I1: forzar 2FA para roles SuperAdmin y Administrador
// Si el admin no tiene 2FA habilitado, se le redirige a configurarlo
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true &&
        (context.User.IsInRole("SuperAdmin") || context.User.IsInRole("Administrador")))
    {
        var path = context.Request.Path.Value ?? "";
        var excluded2fa = new[] { "/Account/Setup2fa", "/Account/Enable2fa", "/Account/Logout",
                                   "/Account/Login", "/Account/AccessDenied", "/api/", "/hubs/",
                                   "/css/", "/js/", "/lib/", "/favicon" };
        if (!excluded2fa.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            var userManager = context.RequestServices
                .GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.GetUserAsync(context.User);
            if (user != null)
            {
                var is2faEnabled = await userManager.GetTwoFactorEnabledAsync(user);
                if (!is2faEnabled)
                {
                    context.Response.Redirect("/Account/Setup2fa?enforced=1");
                    return;
                }
            }
        }
    }
    await next();
});

// Force password change every 30 days
app.UseMiddleware<CloudStationWeb.Services.PasswordExpiryMiddleware>();

// SoloVasos: only allow FunVasos, Account and ApiAuth routes
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true && context.User.IsInRole("SoloVasos"))
    {
        var path = context.Request.Path.Value ?? "";
        var allowed = new[] { "/FunVasos", "/Account", "/ApiAuth" };
        if (!allowed.Any(a => path.StartsWith(a, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.Redirect("/FunVasos");
            return;
        }
    }
    await next();
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Map}/{action=Index}/{id?}");

app.MapHub<CloudStationWeb.Hubs.ChatHub>("/hubs/chat");

app.Run();
