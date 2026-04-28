using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using CloudStationWeb.Models;
using CloudStationWeb.Data;
using CloudStationWeb.Services;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CloudStationWeb.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly ApplicationDbContext _context;
        private readonly UrlEncoder _urlEncoder;
        private readonly string _sqlServerConn;

        private const string AuthenticatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";

        public AccountController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender,
            ApplicationDbContext context,
            UrlEncoder urlEncoder,
            IConfiguration configuration)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _emailSender = emailSender;
            _context = context;
            _urlEncoder = urlEncoder;
            _sqlServerConn = configuration.GetConnectionString("SqlServer") ?? "";
        }

        // ═══════════════════════════════════════════════════════
        //  LOGIN
        // ═══════════════════════════════════════════════════════

        [AllowAnonymous]
        public async Task<IActionResult> Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            ViewData["ExternalLogins"] = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
            ViewData["PendingCount"] = await _userManager.Users.CountAsync(u => !u.IsApproved && u.IsActive);
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("login")] // FIX CVE-A4: limitar intentos de login
        public async Task<IActionResult> Login(string username, string password, bool rememberMe, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            ViewData["ExternalLogins"] = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ModelState.AddModelError(string.Empty, "Usuario y contraseña son requeridos.");
                return View();
            }

            var user = await _userManager.FindByNameAsync(username);
            if (user != null && !user.IsActive)
            {
                await LogAudit(user.Id, username, false, "Cuenta desactivada", "Local");
                ModelState.AddModelError(string.Empty, "Esta cuenta ha sido desactivada. Contacte al administrador.");
                return View();
            }

            if (user != null && !user.IsApproved)
            {
                await LogAudit(user.Id, username, false, "Cuenta pendiente de aprobación", "Local");
                ModelState.AddModelError(string.Empty, "Su solicitud de acceso aún está pendiente de aprobación por el administrador.");
                return View();
            }

            var result = await _signInManager.PasswordSignInAsync(username, password, rememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                // Initialize PasswordLastChanged for existing users that don't have it
                if (user != null && user.PasswordLastChanged == null)
                {
                    user.PasswordLastChanged = DateTime.UtcNow;
                    await _userManager.UpdateAsync(user);
                }

                await LogAudit(user?.Id, username, true, null, "Local");
                return LocalRedirect(returnUrl ?? "/");
            }

            if (result.RequiresTwoFactor)
            {
                return RedirectToAction("LoginWith2fa", new { returnUrl, rememberMe });
            }

            if (result.IsLockedOut)
            {
                await LogAudit(user?.Id, username, false, "Cuenta bloqueada", "Local");
                ModelState.AddModelError(string.Empty, "Cuenta bloqueada por demasiados intentos fallidos. Intente en 15 minutos.");
                return View();
            }

            await LogAudit(null, username, false, "Credenciales incorrectas", "Local");
            ModelState.AddModelError(string.Empty, "Usuario o contraseña incorrectos.");
            return View();
        }

        // ═══════════════════════════════════════════════════════
        //  LOGIN WITH 2FA
        // ═══════════════════════════════════════════════════════

        [AllowAnonymous]
        public IActionResult LoginWith2fa(string? returnUrl = null, bool rememberMe = false)
        {
            ViewData["ReturnUrl"] = returnUrl;
            ViewData["RememberMe"] = rememberMe;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginWith2fa(string twoFactorCode, bool rememberMe, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            ViewData["RememberMe"] = rememberMe;

            if (string.IsNullOrEmpty(twoFactorCode))
            {
                ModelState.AddModelError(string.Empty, "Ingrese el código de verificación.");
                return View();
            }

            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null)
            {
                return RedirectToAction("Login");
            }

            var code = twoFactorCode.Replace(" ", string.Empty).Replace("-", string.Empty);
            var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(code, rememberMe, rememberClient: false);

            if (result.Succeeded)
            {
                await LogAudit(user.Id, user.UserName!, true, null, "2FA");
                return LocalRedirect(returnUrl ?? "/");
            }

            if (result.IsLockedOut)
            {
                await LogAudit(user.Id, user.UserName!, false, "Bloqueado tras 2FA", "2FA");
                ModelState.AddModelError(string.Empty, "Cuenta bloqueada. Intente en 15 minutos.");
                return View();
            }

            await LogAudit(user.Id, user.UserName!, false, "Código 2FA inválido", "2FA");
            ModelState.AddModelError(string.Empty, "Código de verificación inválido.");
            return View();
        }

        // ═══════════════════════════════════════════════════════
        //  LOGOUT
        // ═══════════════════════════════════════════════════════

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login");
        }

        // ═══════════════════════════════════════════════════════
        //  REGISTER (Solicitud de acceso)
        // ═══════════════════════════════════════════════════════

        [AllowAnonymous]
        public async Task<IActionResult> Register()
        {
            await LoadRegisterDataAsync();
            return View();
        }

        private async Task LoadRegisterDataAsync()
        {
            using var db = new SqlConnection(_sqlServerConn);
            var organismos = (await db.QueryAsync<dynamic>("SELECT Id, Nombre FROM Organismo WHERE Activo = 1 ORDER BY Nombre")).ToList();
            ViewBag.Organismos = organismos;
            ViewBag.CentrosTrabajo = await _context.CentrosTrabajo.Where(c => c.Activo).OrderBy(c => c.Nombre).ToListAsync();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("register")] // FIX CVE-A4: limitar solicitudes de registro masivo
        public async Task<IActionResult> Register(string username, string fullName, string email, string password, string confirmPassword, string? registrationNote, int? organismoId, int? centroTrabajoId, bool esTrabajadorCFE = true, string? empresaExterna = null, string? departamentoExterno = null)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(fullName))
            {
                ModelState.AddModelError(string.Empty, "Usuario, nombre completo y contraseña son requeridos.");
                await LoadRegisterDataAsync();
                return View();
            }

            if (password != confirmPassword)
            {
                ModelState.AddModelError(string.Empty, "Las contraseñas no coinciden.");
                await LoadRegisterDataAsync();
                return View();
            }

            var existingUser = await _userManager.FindByNameAsync(username);
            if (existingUser != null)
            {
                ModelState.AddModelError(string.Empty, "Este nombre de usuario ya está en uso.");
                await LoadRegisterDataAsync();
                return View();
            }

            var user = new ApplicationUser
            {
                UserName = username,
                Email = email,
                FullName = fullName,
                IsActive = true,
                IsApproved = false,
                RegistrationNote = registrationNote?.Trim(),
                EsTrabajadorCFE = esTrabajadorCFE,
                OrganismoId = esTrabajadorCFE ? organismoId : null,
                CentroTrabajoId = esTrabajadorCFE ? centroTrabajoId : null,
                EmpresaExterna = !esTrabajadorCFE ? empresaExterna?.Trim() : null,
                DepartamentoExterno = !esTrabajadorCFE ? departamentoExterno?.Trim() : null,
                CreatedAt = DateTime.UtcNow,
                PasswordLastChanged = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(user, "Visualizador");
                await LogAudit(user.Id, username, true, "Registro - pendiente aprobación", "Register");

                // Notificar a administradores por email
                try
                {
                    var admins = await _userManager.GetUsersInRoleAsync("Administrador");
                    foreach (var admin in admins)
                    {
                        if (!string.IsNullOrEmpty(admin.Email))
                        {
                            var htmlBody = $@"
                                <div style='font-family: Inter, Arial, sans-serif; max-width: 500px; margin: 0 auto; background: #1a1f2e; border-radius: 16px; padding: 40px; color: #ffffff;'>
                                    <div style='text-align: center; margin-bottom: 30px;'>
                                        <div style='display: inline-block; width: 48px; height: 48px; background: linear-gradient(135deg, #f59e0b, #d97706); border-radius: 12px; line-height: 48px; font-size: 24px;'>👤</div>
                                        <h2 style='color: #ffffff; margin-top: 16px;'>Nueva Solicitud de Acceso</h2>
                                    </div>
                                    <p style='color: rgba(255,255,255,0.7); font-size: 14px;'>Se ha recibido una nueva solicitud de acceso al sistema PIH:</p>
                                    <div style='background: rgba(255,255,255,0.06); border-radius: 12px; padding: 20px; margin: 20px 0;'>
                                        <p style='color: rgba(255,255,255,0.6); font-size: 13px; margin: 8px 0;'><strong style='color:#fff;'>Usuario:</strong> {user.UserName}</p>
                                        <p style='color: rgba(255,255,255,0.6); font-size: 13px; margin: 8px 0;'><strong style='color:#fff;'>Nombre:</strong> {user.FullName}</p>
                                        <p style='color: rgba(255,255,255,0.6); font-size: 13px; margin: 8px 0;'><strong style='color:#fff;'>Email:</strong> {user.Email ?? "No proporcionado"}</p>
                                        <p style='color: rgba(255,255,255,0.6); font-size: 13px; margin: 8px 0;'><strong style='color:#fff;'>Motivo:</strong> {user.RegistrationNote ?? "No especificado"}</p>
                                    </div>
                                    <p style='color: rgba(255,255,255,0.5); font-size: 13px;'>Ingrese al panel de administración para aprobar o rechazar esta solicitud.</p>
                                    <hr style='border: none; border-top: 1px solid rgba(255,255,255,0.08); margin: 24px 0;'>
                                    <p style='color: rgba(255,255,255,0.3); font-size: 11px; text-align: center;'>© 2026 Plataforma Integral Hidrometeorológica • CFE</p>
                                </div>";
                            await _emailSender.SendEmailAsync(admin.Email, "Nueva Solicitud de Acceso - PIH", htmlBody);
                        }
                    }
                }
                catch { /* No bloquear el registro si falla el email */ }

                return RedirectToAction("RegistrationPending");
            }

            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            await LoadRegisterDataAsync();
            return View();
        }

        [AllowAnonymous]
        public IActionResult RegistrationPending() => View();

        // ═══════════════════════════════════════════════════════
        //  FORGOT / RESET PASSWORD
        // ═══════════════════════════════════════════════════════

        [AllowAnonymous]
        public IActionResult ForgotPassword() => View();

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                ModelState.AddModelError(string.Empty, "Ingrese su correo electrónico.");
                return View();
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null || !user.IsActive)
            {
                return View("ForgotPasswordConfirmation");
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var callbackUrl = Url.Action("ResetPassword", "Account",
                new { email = user.Email, token }, protocol: Request.Scheme);

            var htmlBody = $@"
                <div style='font-family: Inter, Arial, sans-serif; max-width: 500px; margin: 0 auto; background: #1a1f2e; border-radius: 16px; padding: 40px; color: #ffffff;'>
                    <div style='text-align: center; margin-bottom: 30px;'>
                        <div style='display: inline-block; width: 48px; height: 48px; background: linear-gradient(135deg, #3b82f6, #6366f1); border-radius: 12px; line-height: 48px; font-size: 24px;'>⚡</div>
                        <h2 style='color: #ffffff; margin-top: 16px;'>PIH</h2>
                    </div>
                    <p style='color: rgba(255,255,255,0.7); font-size: 14px;'>Hola <strong style='color:#fff;'>{user.FullName}</strong>,</p>
                    <p style='color: rgba(255,255,255,0.7); font-size: 14px;'>Recibimos una solicitud para restablecer su contraseña:</p>
                    <div style='text-align: center; margin: 30px 0;'>
                        <a href='{callbackUrl}' style='display: inline-block; padding: 14px 36px; background: linear-gradient(135deg, #3b82f6, #6366f1); color: #fff; text-decoration: none; border-radius: 10px; font-weight: 600; font-size: 15px;'>Restablecer Contraseña</a>
                    </div>
                    <p style='color: rgba(255,255,255,0.4); font-size: 12px;'>Si no solicitó este cambio, puede ignorar este correo. El enlace expirará automáticamente.</p>
                    <hr style='border: none; border-top: 1px solid rgba(255,255,255,0.08); margin: 24px 0;'>
                    <p style='color: rgba(255,255,255,0.3); font-size: 11px; text-align: center;'>© 2026 Plataforma Integral Hidrometeorológica • CFE</p>
                </div>";

            await _emailSender.SendEmailAsync(email, "Restablecer Contraseña - PIH", htmlBody);
            return View("ForgotPasswordConfirmation");
        }

        [AllowAnonymous]
        public IActionResult ForgotPasswordConfirmation() => View();

        [AllowAnonymous]
        public IActionResult ResetPassword(string? email, string? token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
                return RedirectToAction("Login");
            ViewData["Email"] = email;
            ViewData["Token"] = token;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string email, string token, string password, string confirmPassword)
        {
            ViewData["Email"] = email;
            ViewData["Token"] = token;

            if (string.IsNullOrEmpty(password))
            {
                ModelState.AddModelError(string.Empty, "La contraseña es requerida.");
                return View();
            }

            if (password != confirmPassword)
            {
                ModelState.AddModelError(string.Empty, "Las contraseñas no coinciden.");
                return View();
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
                return View("ResetPasswordConfirmation");

            var result = await _userManager.ResetPasswordAsync(user, token, password);
            if (result.Succeeded)
                return View("ResetPasswordConfirmation");

            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View();
        }

        [AllowAnonymous]
        public IActionResult ResetPasswordConfirmation() => View();

        // ═══════════════════════════════════════════════════════
        //  EXTERNAL LOGIN (Google, Microsoft)
        // ═══════════════════════════════════════════════════════

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public IActionResult ExternalLogin(string provider, string? returnUrl = null)
        {
            var redirectUrl = Url.Action("ExternalLoginCallback", "Account", new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return Challenge(properties, provider);
        }

        [AllowAnonymous]
        public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null)
        {
            if (remoteError != null)
            {
                ModelState.AddModelError(string.Empty, $"Error del proveedor externo: {remoteError}");
                return RedirectToAction("Login");
            }

            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
                return RedirectToAction("Login");

            var signInResult = await _signInManager.ExternalLoginSignInAsync(
                info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: false);

            if (signInResult.Succeeded)
            {
                var existingUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
                if (existingUser != null && !existingUser.IsApproved)
                {
                    await _signInManager.SignOutAsync();
                    await LogAudit(existingUser.Id, existingUser.UserName ?? "external", false, "Cuenta pendiente de aprobación", info.LoginProvider);
                    TempData["ExternalError"] = "Su solicitud de acceso aún está pendiente de aprobación.";
                    return RedirectToAction("Login");
                }
                await LogAudit(existingUser?.Id, existingUser?.UserName ?? "external", true, null, info.LoginProvider);
                return LocalRedirect(returnUrl ?? "/");
            }

            if (signInResult.RequiresTwoFactor)
            {
                return RedirectToAction("LoginWith2fa", new { returnUrl });
            }

            var externalEmail = info.Principal.FindFirstValue(ClaimTypes.Email);
            var name = info.Principal.FindFirstValue(ClaimTypes.Name) ?? externalEmail;

            if (externalEmail == null)
            {
                ModelState.AddModelError(string.Empty, "No se pudo obtener el email del proveedor externo.");
                return RedirectToAction("Login");
            }

            var user = await _userManager.FindByEmailAsync(externalEmail);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = externalEmail,
                    Email = externalEmail,
                    EmailConfirmed = true,
                    FullName = name ?? externalEmail,
                    IsActive = true,
                    IsApproved = false,
                    RegistrationNote = $"Registro vía {info.LoginProvider}",
                    CreatedAt = DateTime.UtcNow,
                    PasswordLastChanged = DateTime.UtcNow
                };

                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    ModelState.AddModelError(string.Empty, "Error al crear la cuenta.");
                    return RedirectToAction("Login");
                }
                await _userManager.AddToRoleAsync(user, "Visualizador");
            }

            await _userManager.AddLoginAsync(user, info);

            if (!user.IsApproved)
            {
                await LogAudit(user.Id, user.UserName!, true, "Registro externo - pendiente aprobación", info.LoginProvider);
                return RedirectToAction("RegistrationPending");
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
            await LogAudit(user.Id, user.UserName!, true, null, info.LoginProvider);

            return LocalRedirect(returnUrl ?? "/");
        }

        // ═══════════════════════════════════════════════════════
        //  PROFILE (Mi Perfil)
        // ═══════════════════════════════════════════════════════

        [Authorize]
        public async Task<IActionResult> Profile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");

            var roles = await _userManager.GetRolesAsync(user);
            var is2faEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
            var logins = await _userManager.GetLoginsAsync(user);

            var recentLogins = await _context.LoginAudits
                .Where(l => l.UserId == user.Id)
                .OrderByDescending(l => l.Timestamp)
                .Take(10)
                .ToListAsync();

            // Cargar nombre del organismo
            string organismoNombre = "No asignado";
            if (user.OrganismoId.HasValue && !string.IsNullOrEmpty(_sqlServerConn))
            {
                using var db = new SqlConnection(_sqlServerConn);
                organismoNombre = await db.QueryFirstOrDefaultAsync<string>(
                    "SELECT Nombre FROM Organismo WHERE Id = @Id",
                    new { Id = user.OrganismoId.Value }) ?? "No asignado";
            }

            // Cargar centro de trabajo
            string centroTrabajoNombre = "No asignado";
            if (user.CentroTrabajoId.HasValue)
            {
                var ct = await _context.CentrosTrabajo.FindAsync(user.CentroTrabajoId.Value);
                centroTrabajoNombre = ct?.Nombre ?? "No asignado";
            }

            ViewBag.User = user;
            ViewBag.Role = roles.FirstOrDefault() ?? "Sin rol";
            ViewBag.Is2faEnabled = is2faEnabled;
            ViewBag.ExternalLogins = logins;
            ViewBag.RecentLogins = recentLogins;
            ViewBag.OrganismoNombre = organismoNombre;
            ViewBag.CentroTrabajoNombre = centroTrabajoNombre;

            return View();
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmNewPassword)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");

            if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword))
            {
                TempData["Error"] = "Todos los campos son requeridos.";
                return RedirectToAction("Profile");
            }

            if (newPassword != confirmNewPassword)
            {
                TempData["Error"] = "Las contraseñas nuevas no coinciden.";
                return RedirectToAction("Profile");
            }

            var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
            if (result.Succeeded)
            {
                user.PasswordLastChanged = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);
                await _signInManager.RefreshSignInAsync(user);
                TempData["Success"] = "Contraseña actualizada correctamente.";
            }
            else
            {
                TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction("Profile");
        }

        // ═══════════════════════════════════════════════════════
        //  FORCE CHANGE PASSWORD (expired)
        // ═══════════════════════════════════════════════════════

        [Authorize]
        public async Task<IActionResult> ForceChangePassword()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");

            var lastChanged = user.PasswordLastChanged ?? user.CreatedAt;
            var age = (DateTime.UtcNow - lastChanged).TotalDays;
            ViewBag.DaysExpired = Math.Max(0, (int)age - 30);
            return View();
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForceChangePassword(string currentPassword, string newPassword, string confirmNewPassword)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");

            if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword))
            {
                TempData["Error"] = "Todos los campos son requeridos.";
                return RedirectToAction("ForceChangePassword");
            }

            if (newPassword != confirmNewPassword)
            {
                TempData["Error"] = "Las contraseñas nuevas no coinciden.";
                return RedirectToAction("ForceChangePassword");
            }

            var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
            if (result.Succeeded)
            {
                user.PasswordLastChanged = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);
                await _signInManager.RefreshSignInAsync(user);
                TempData["Success"] = "Contraseña actualizada. Ya puede usar el sistema.";
                return RedirectToAction("Index", "Map");
            }

            TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToAction("ForceChangePassword");
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(string fullName, string email)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");

            if (!string.IsNullOrWhiteSpace(fullName))
                user.FullName = fullName.Trim();

            if (!string.IsNullOrWhiteSpace(email))
            {
                user.Email = email.Trim();
                user.NormalizedEmail = email.Trim().ToUpperInvariant();
            }

            await _userManager.UpdateAsync(user);
            TempData["Success"] = "Perfil actualizado correctamente.";
            return RedirectToAction("Profile");
        }

        // ═══════════════════════════════════════════════════════
        //  2FA SETUP
        // ═══════════════════════════════════════════════════════

        [Authorize]
        public async Task<IActionResult> Setup2fa()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");

            var is2faEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
            if (is2faEnabled)
            {
                ViewBag.Is2faEnabled = true;
                return View();
            }

            await _userManager.ResetAuthenticatorKeyAsync(user);
            var unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(unformattedKey))
            {
                await _userManager.ResetAuthenticatorKeyAsync(user);
                unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
            }

            ViewBag.SharedKey = FormatKey(unformattedKey!);
            ViewBag.AuthenticatorUri = GenerateQrCodeUri(user.UserName!, unformattedKey!);
            ViewBag.Is2faEnabled = false;

            return View();
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Enable2fa(string verificationCode)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");

            if (string.IsNullOrEmpty(verificationCode))
            {
                TempData["Error"] = "Ingrese el código de verificación.";
                return RedirectToAction("Setup2fa");
            }

            var code = verificationCode.Replace(" ", string.Empty).Replace("-", string.Empty);
            var is2faTokenValid = await _userManager.VerifyTwoFactorTokenAsync(
                user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);

            if (!is2faTokenValid)
            {
                TempData["Error"] = "Código de verificación inválido. Vuelva a escanear el QR e intente de nuevo.";
                return RedirectToAction("Setup2fa");
            }

            await _userManager.SetTwoFactorEnabledAsync(user, true);
            TempData["Success"] = "Autenticación de dos factores habilitada exitosamente.";
            return RedirectToAction("Profile");
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Disable2fa()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");

            await _userManager.SetTwoFactorEnabledAsync(user, false);
            await _userManager.ResetAuthenticatorKeyAsync(user);
            await _signInManager.RefreshSignInAsync(user);

            TempData["Success"] = "Autenticación de dos factores deshabilitada.";
            return RedirectToAction("Profile");
        }

        // ═══════════════════════════════════════════════════════
        //  ACCESS DENIED
        // ═══════════════════════════════════════════════════════

        [AllowAnonymous]
        public IActionResult AccessDenied() => View();

        // ═══════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════

        private async Task LogAudit(string? userId, string userName, bool success, string? failureReason, string? provider)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var ua = Request.Headers.UserAgent.ToString();
            if (ua.Length > 500) ua = ua[..500];

            _context.LoginAudits.Add(new LoginAudit
            {
                UserId = userId,
                UserName = userName,
                Timestamp = DateTime.UtcNow,
                Success = success,
                FailureReason = failureReason,
                IpAddress = ip,
                UserAgent = ua,
                Provider = provider
            });
            await _context.SaveChangesAsync();
        }

        private static string FormatKey(string unformattedKey)
        {
            var sb = new StringBuilder();
            int currentPosition = 0;
            while (currentPosition + 4 < unformattedKey.Length)
            {
                sb.Append(unformattedKey.AsSpan(currentPosition, 4)).Append(' ');
                currentPosition += 4;
            }
            if (currentPosition < unformattedKey.Length)
                sb.Append(unformattedKey.AsSpan(currentPosition));
            return sb.ToString().ToLowerInvariant();
        }

        private string GenerateQrCodeUri(string userName, string unformattedKey)
        {
            return string.Format(
                AuthenticatorUriFormat,
                _urlEncoder.Encode("PIH-CFE"),
                _urlEncoder.Encode(userName),
                unformattedKey);
        }
    }
}
