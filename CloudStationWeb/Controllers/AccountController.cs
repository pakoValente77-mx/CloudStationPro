using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using CloudStationWeb.Models;
using CloudStationWeb.Services;

namespace CloudStationWeb.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;

        public AccountController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _emailSender = emailSender;
        }

        // GET: /Account/Login
        [AllowAnonymous]
        public async Task<IActionResult> Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            ViewData["ExternalLogins"] = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
            return View();
        }

        // POST: /Account/Login
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
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
                ModelState.AddModelError(string.Empty, "Esta cuenta ha sido desactivada. Contacte al administrador.");
                return View();
            }

            var result = await _signInManager.PasswordSignInAsync(username, password, rememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                return LocalRedirect(returnUrl ?? "/");
            }

            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "Cuenta bloqueada por demasiados intentos fallidos. Intente en 15 minutos.");
                return View();
            }

            ModelState.AddModelError(string.Empty, "Usuario o contraseña incorrectos.");
            return View();
        }

        // POST: /Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login");
        }

        // GET: /Account/AccessDenied
        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }

        // GET: /Account/ForgotPassword
        [AllowAnonymous]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        // POST: /Account/ForgotPassword
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
                // Don't reveal that the user does not exist
                return View("ForgotPasswordConfirmation");
            }

            // Generate reset token
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var callbackUrl = Url.Action("ResetPassword", "Account",
                new { email = user.Email, token }, protocol: Request.Scheme);

            // Send email
            var htmlBody = $@"
                <div style='font-family: Inter, Arial, sans-serif; max-width: 500px; margin: 0 auto; background: #1a1f2e; border-radius: 16px; padding: 40px; color: #ffffff;'>
                    <div style='text-align: center; margin-bottom: 30px;'>
                        <div style='display: inline-block; width: 48px; height: 48px; background: linear-gradient(135deg, #3b82f6, #6366f1); border-radius: 12px; line-height: 48px; font-size: 24px;'>⚡</div>
                        <h2 style='color: #ffffff; margin-top: 16px;'>PIH</h2>
                    </div>
                    <p style='color: rgba(255,255,255,0.7); font-size: 14px;'>Hola <strong style='color:#fff;'>{user.FullName}</strong>,</p>
                    <p style='color: rgba(255,255,255,0.7); font-size: 14px;'>Recibimos una solicitud para restablecer su contraseña. Haga clic en el siguiente botón para crear una nueva contraseña:</p>
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

        // GET: /Account/ForgotPasswordConfirmation
        [AllowAnonymous]
        public IActionResult ForgotPasswordConfirmation()
        {
            return View();
        }

        // GET: /Account/ResetPassword
        [AllowAnonymous]
        public IActionResult ResetPassword(string? email, string? token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            {
                return RedirectToAction("Login");
            }
            ViewData["Email"] = email;
            ViewData["Token"] = token;
            return View();
        }

        // POST: /Account/ResetPassword
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
            {
                // Don't reveal that the user doesn't exist
                return View("ResetPasswordConfirmation");
            }

            var result = await _userManager.ResetPasswordAsync(user, token, password);
            if (result.Succeeded)
            {
                return View("ResetPasswordConfirmation");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View();
        }

        // GET: /Account/ResetPasswordConfirmation
        [AllowAnonymous]
        public IActionResult ResetPasswordConfirmation()
        {
            return View();
        }

        // POST: /Account/ExternalLogin
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public IActionResult ExternalLogin(string provider, string? returnUrl = null)
        {
            var redirectUrl = Url.Action("ExternalLoginCallback", "Account", new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return Challenge(properties, provider);
        }

        // GET: /Account/ExternalLoginCallback
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
            {
                return RedirectToAction("Login");
            }

            // Try to sign in with external login
            var signInResult = await _signInManager.ExternalLoginSignInAsync(
                info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);

            if (signInResult.Succeeded)
            {
                return LocalRedirect(returnUrl ?? "/");
            }

            // If user doesn't exist, create one
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
                    CreatedAt = DateTime.UtcNow
                };

                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    ModelState.AddModelError(string.Empty, "Error al crear la cuenta.");
                    return RedirectToAction("Login");
                }

                // Assign default role
                await _userManager.AddToRoleAsync(user, "Visualizador");
            }

            // Link external login to user
            await _userManager.AddLoginAsync(user, info);
            await _signInManager.SignInAsync(user, isPersistent: false);

            return LocalRedirect(returnUrl ?? "/");
        }
    }
}
