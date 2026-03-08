using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CloudStationWeb.Models;
using CloudStationWeb.Data;

namespace CloudStationWeb.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class AdminController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;

        public AdminController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
        }

        // GET: /Admin/UserPermissions/userId
        public async Task<IActionResult> UserPermissions(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            var products = await _context.DocumentProducts
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync();

            var userPermissions = await _context.UserProductPermissions
                .Where(up => up.UserId == userId)
                .Select(up => up.ProductId)
                .ToListAsync();

            ViewBag.User = user;
            ViewBag.AssignedProductIds = userPermissions;

            return View(products);
        }

        // POST: /Admin/UpdateUserPermissions
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateUserPermissions(string userId, List<int> productIds)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            // Remove all existing permissions for this user
            var existing = _context.UserProductPermissions.Where(up => up.UserId == userId);
            _context.UserProductPermissions.RemoveRange(existing);

            // Add new permissions
            if (productIds != null)
            {
                foreach (var pid in productIds)
                {
                    _context.UserProductPermissions.Add(new UserProductPermission
                    {
                        UserId = userId,
                        ProductId = pid
                    });
                }
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = $"Permisos de '{user.UserName}' actualizados correctamente.";

            return RedirectToAction("Index");
        }

        // GET: /Admin
        public async Task<IActionResult> Index()
        {
            var users = await _userManager.Users.OrderBy(u => u.UserName).ToListAsync();
            var userRoles = new Dictionary<string, IList<string>>();

            foreach (var user in users)
            {
                userRoles[user.Id] = await _userManager.GetRolesAsync(user);
            }

            ViewBag.Roles = await _roleManager.Roles.Select(r => r.Name).ToListAsync();
            ViewBag.UserRoles = userRoles;
            return View(users);
        }

        // POST: /Admin/CreateUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(string username, string fullName, string email, string password, string role)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(role))
            {
                TempData["Error"] = "Todos los campos obligatorios deben ser completados.";
                return RedirectToAction("Index");
            }

            var user = new ApplicationUser
            {
                UserName = username,
                Email = email,
                FullName = fullName ?? username,
                EmailConfirmed = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(user, role);
                TempData["Success"] = $"Usuario '{username}' creado exitosamente con rol '{role}'.";
            }
            else
            {
                TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction("Index");
        }

        // POST: /Admin/ToggleUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "Usuario no encontrado.";
                return RedirectToAction("Index");
            }

            // Prevent disabling self
            if (user.UserName == User.Identity?.Name)
            {
                TempData["Error"] = "No puede desactivar su propia cuenta.";
                return RedirectToAction("Index");
            }

            user.IsActive = !user.IsActive;
            await _userManager.UpdateAsync(user);
            TempData["Success"] = $"Usuario '{user.UserName}' {(user.IsActive ? "activado" : "desactivado")}.";

            return RedirectToAction("Index");
        }

        // POST: /Admin/ChangeRole
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeRole(string userId, string newRole)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "Usuario no encontrado.";
                return RedirectToAction("Index");
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, newRole);
            TempData["Success"] = $"Rol de '{user.UserName}' cambiado a '{newRole}'.";

            return RedirectToAction("Index");
        }

        // POST: /Admin/DeleteUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "Usuario no encontrado.";
                return RedirectToAction("Index");
            }

            if (user.UserName == "administrador")
            {
                TempData["Error"] = "No se puede eliminar al super administrador.";
                return RedirectToAction("Index");
            }

            if (user.UserName == User.Identity?.Name)
            {
                TempData["Error"] = "No puede eliminar su propia cuenta.";
                return RedirectToAction("Index");
            }

            await _userManager.DeleteAsync(user);
            TempData["Success"] = $"Usuario '{user.UserName}' eliminado.";

            return RedirectToAction("Index");
        }

        // POST: /Admin/ResetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string userId, string newPassword)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "Usuario no encontrado.";
                return RedirectToAction("Index");
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

            if (result.Succeeded)
            {
                TempData["Success"] = $"Contraseña de '{user.UserName}' restablecida.";
            }
            else
            {
                TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction("Index");
        }
    }
}
