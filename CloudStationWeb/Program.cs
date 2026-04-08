using OfficeOpenXml;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
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

// Configure cookie authentication
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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
builder.Services.AddScoped<CloudStationWeb.Services.HydroForecastService>();
builder.Services.AddScoped<CloudStationWeb.Services.IEmailSender, CloudStationWeb.Services.SmtpEmailSender>();
builder.Services.AddSingleton<CloudStationWeb.Services.PushNotificationService>();
builder.Services.AddSingleton<CloudStationWeb.Services.ChartService>();
builder.Services.AddSingleton<CloudStationWeb.Services.CentinelaBotService>();
builder.Services.AddSingleton<CloudStationWeb.Services.EarlyWarningService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CloudStationWeb.Services.EarlyWarningService>());
builder.Services.AddSingleton<CloudStationWeb.Services.PrecipitationAlertService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CloudStationWeb.Services.PrecipitationAlertService>());

// CORS for API consumers (mobile, desktop apps)
builder.Services.AddCors(options =>
{
    options.AddPolicy("ApiCors", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
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

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".kml"] = "application/vnd.google-earth.kml+xml";
provider.Mappings[".kmz"] = "application/vnd.google-earth.kmz";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider
});

app.UseRouting();

app.UseCors("ApiCors");
app.UseAuthentication();
app.UseAuthorization();

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
