using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CloudStationWeb.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace CloudStationWeb.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class ApiAuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IConfiguration _config;

        public ApiAuthController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IConfiguration config)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _config = config;
        }

        public class LoginRequest
        {
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
        }

        /// <summary>
        /// Autentica un usuario y devuelve un JWT token para consumo desde apps.
        /// POST /api/auth/login  { "username": "...", "password": "..." }
        /// </summary>
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { error = "Usuario y contraseña son requeridos." });

            var user = await _userManager.FindByNameAsync(request.Username);
            if (user == null)
                return Unauthorized(new { error = "Credenciales inválidas." });

            if (!user.IsActive)
                return Unauthorized(new { error = "Cuenta desactivada." });

            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
            if (result.IsLockedOut)
                return Unauthorized(new { error = "Cuenta bloqueada por intentos fallidos. Intente en 15 minutos." });

            if (!result.Succeeded)
                return Unauthorized(new { error = "Credenciales inválidas." });

            var roles = await _userManager.GetRolesAsync(user);
            var token = GenerateJwtToken(user, roles);

            return Ok(new
            {
                token,
                expira = DateTime.UtcNow.AddHours(double.Parse(_config["Jwt:ExpireHours"] ?? "24")),
                usuario = user.UserName,
                nombre = user.FullName,
                roles = roles,
            });
        }

        private string GenerateJwtToken(ApplicationUser user, IList<string> roles)
        {
            var jwtKey = _config["Jwt:Key"]!;
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id),
                new(ClaimTypes.Name, user.UserName ?? ""),
                new("fullName", user.FullName ?? ""),
            };
            foreach (var role in roles)
                claims.Add(new Claim(ClaimTypes.Role, role));

            var expireHours = double.Parse(_config["Jwt:ExpireHours"] ?? "24");
            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(expireHours),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
