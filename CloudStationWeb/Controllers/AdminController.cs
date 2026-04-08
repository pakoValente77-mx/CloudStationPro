using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Dapper;
using CloudStationWeb.Models;
using CloudStationWeb.Data;
using CloudStationWeb.Services;

namespace CloudStationWeb.Controllers
{
    [Authorize(Roles = "SuperAdmin,Administrador")]
    public class AdminController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;
        private readonly IEmailSender _emailSender;
        private readonly string _sqlServerConn;

        public AdminController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext context,
            IEmailSender emailSender,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
            _emailSender = emailSender;
            _sqlServerConn = configuration.GetConnectionString("SqlServer")!;
        }

        private bool IsSuperAdmin => User.IsInRole("SuperAdmin");

        private async Task<ApplicationUser?> GetCurrentUserAsync()
            => await _userManager.GetUserAsync(User);

        /// <summary>
        /// Validates that the current non-SuperAdmin user belongs to the same Organismo as the target user.
        /// Returns true if access is allowed, false if it should be denied.
        /// </summary>
        private async Task<bool> CanManageUserAsync(ApplicationUser targetUser)
        {
            if (IsSuperAdmin) return true;
            var me = await GetCurrentUserAsync();
            return me?.OrganismoId != null && me.OrganismoId == targetUser.OrganismoId;
        }

        // GET: /Admin/UserPermissions/userId
        public async Task<IActionResult> UserPermissions(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            if (!await CanManageUserAsync(user)) return Forbid();

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

            if (!await CanManageUserAsync(user)) return Forbid();

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
            var currentUser = await GetCurrentUserAsync();
            var isSuperAdmin = IsSuperAdmin;
            var currentOrganismoId = currentUser?.OrganismoId;

            // Load all users, then filter by organismo if not SuperAdmin
            var allUsers = await _userManager.Users.OrderBy(u => u.UserName).ToListAsync();
            var users = isSuperAdmin
                ? allUsers
                : allUsers.Where(u => u.OrganismoId == currentOrganismoId).ToList();

            var userRoles = new Dictionary<string, IList<string>>();
            var user2fa = new Dictionary<string, bool>();
            var userLastLogin = new Dictionary<string, DateTime?>();

            // Fetch last successful login per user
            var lastLogins = await _context.Set<CloudStationWeb.Models.LoginAudit>()
                .Where(l => l.Success && l.UserId != null)
                .GroupBy(l => l.UserId!)
                .Select(g => new { UserId = g.Key, LastLogin = g.Max(l => l.Timestamp) })
                .ToListAsync();
            var lastLoginMap = lastLogins.ToDictionary(x => x.UserId, x => (DateTime?)x.LastLogin);

            foreach (var user in users)
            {
                userRoles[user.Id] = await _userManager.GetRolesAsync(user);
                user2fa[user.Id] = await _userManager.GetTwoFactorEnabledAsync(user);
                userLastLogin[user.Id] = lastLoginMap.GetValueOrDefault(user.Id);
            }

            // Roles disponibles: SuperAdmin solo puede asignar el SuperAdmin, Admin no
            var availableRoles = isSuperAdmin
                ? await _roleManager.Roles.Select(r => r.Name).ToListAsync()
                : await _roleManager.Roles.Where(r => r.Name != "SuperAdmin").Select(r => r.Name).ToListAsync();

            ViewBag.Roles = availableRoles;
            ViewBag.UserRoles = userRoles;
            ViewBag.User2fa = user2fa;
            ViewBag.UserLastLogin = userLastLogin;
            ViewBag.IsSuperAdmin = isSuperAdmin;

            // Organismos (desde SQL Server via Dapper)
            List<CatalogItemInt> organismos;
            using (var db = new SqlConnection(_sqlServerConn))
            {
                organismos = (await db.QueryAsync<CatalogItemInt>(
                    "SELECT Id, Nombre FROM Organismo ORDER BY Nombre")).ToList();
            }
            ViewBag.Organismos = organismos;
            ViewBag.CurrentOrganismoId = currentOrganismoId;

            // Centros de Trabajo
            ViewBag.CentrosTrabajo = await _context.CentrosTrabajo.Where(c => c.Activo).OrderBy(c => c.Nombre).ToListAsync();

            // Solicitudes pendientes de aprobación (filtradas por organismo)
            var pendingQuery = _userManager.Users.Where(u => !u.IsApproved && u.IsActive);
            if (!isSuperAdmin)
                pendingQuery = pendingQuery.Where(u => u.OrganismoId == currentOrganismoId);
            var pendingUsers = await pendingQuery.OrderByDescending(u => u.CreatedAt).ToListAsync();
            var pendingRoles = new Dictionary<string, IList<string>>();
            foreach (var pu in pendingUsers)
                pendingRoles[pu.Id] = await _userManager.GetRolesAsync(pu);
            ViewBag.PendingUsers = pendingUsers;
            ViewBag.PendingRoles = pendingRoles;

            return View(users);
        }

        // POST: /Admin/CreateUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(string username, string fullName, string email, string password, string role, int? organismoId, int? centroTrabajoId)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(role))
            {
                TempData["Error"] = "Todos los campos obligatorios deben ser completados.";
                return RedirectToAction("Index");
            }

            // Non-SuperAdmin can only create users in their own organismo
            if (!IsSuperAdmin)
            {
                var me = await GetCurrentUserAsync();
                organismoId = me?.OrganismoId;
            }

            // Non-SuperAdmin cannot assign SuperAdmin role
            if (!IsSuperAdmin && role == "SuperAdmin")
            {
                role = "Administrador";
            }

            var user = new ApplicationUser
            {
                UserName = username,
                Email = email,
                FullName = fullName ?? username,
                EmailConfirmed = true,
                IsActive = true,
                IsApproved = true,
                ApprovedBy = User.Identity?.Name,
                ApprovedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                PasswordLastChanged = DateTime.UtcNow,
                OrganismoId = organismoId,
                CentroTrabajoId = centroTrabajoId
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

            if (!await CanManageUserAsync(user)) return Forbid();

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

            if (!await CanManageUserAsync(user)) return Forbid();

            // Prevent non-SuperAdmin from escalating to SuperAdmin
            if (!IsSuperAdmin && newRole == "SuperAdmin")
            {
                TempData["Error"] = "No tiene permisos para asignar el rol SuperAdmin.";
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

            if (!await CanManageUserAsync(user)) return Forbid();

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

            if (!await CanManageUserAsync(user)) return Forbid();

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

            if (result.Succeeded)
            {
                user.PasswordLastChanged = null; // Force change on next login
                await _userManager.UpdateAsync(user);
                TempData["Success"] = $"Contraseña de '{user.UserName}' restablecida. El usuario deberá cambiarla en su próximo inicio de sesión.";
            }
            else
            {
                TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction("Index");
        }

        // GET: /Admin/AuditLog
        public async Task<IActionResult> AuditLog(int page = 1, string? search = null, string? filter = null)
        {
            const int pageSize = 50;

            var query = _context.Set<CloudStationWeb.Models.LoginAudit>().AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                var s = search.ToLower();
                query = query.Where(l => l.UserName.ToLower().Contains(s) || (l.IpAddress != null && l.IpAddress.Contains(s)));
            }

            if (filter == "success") query = query.Where(l => l.Success);
            else if (filter == "failed") query = query.Where(l => !l.Success);

            var total = await query.CountAsync();
            var logs = await query
                .OrderByDescending(l => l.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Logs = logs;
            ViewBag.Page = page;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
            ViewBag.Search = search;
            ViewBag.Filter = filter;
            ViewBag.TotalRecords = total;

            return View();
        }

        // POST: /Admin/Toggle2fa
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle2fa(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "Usuario no encontrado.";
                return RedirectToAction("Index");
            }

            if (!await CanManageUserAsync(user)) return Forbid();

            var is2fa = await _userManager.GetTwoFactorEnabledAsync(user);
            if (is2fa)
            {
                await _userManager.SetTwoFactorEnabledAsync(user, false);
                await _userManager.ResetAuthenticatorKeyAsync(user);
                TempData["Success"] = $"2FA deshabilitado para '{user.UserName}'.";
            }
            else
            {
                TempData["Error"] = "El usuario debe habilitar 2FA desde su perfil.";
            }

            return RedirectToAction("Index");
        }

        // POST: /Admin/SendNotification
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendNotification(string? userId, string subject, string message, bool sendToAll = false)
        {
            if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(message))
            {
                TempData["Error"] = "Asunto y mensaje son requeridos.";
                return RedirectToAction("Index");
            }

            var recipients = new List<(string Email, string Name)>();

            if (sendToAll)
            {
                var users = await _userManager.Users
                    .Where(u => u.IsApproved && u.IsActive && u.Email != null && u.Email != "")
                    .Select(u => new { u.Email, u.FullName })
                    .ToListAsync();
                recipients = users.Select(u => (u.Email!, u.FullName)).ToList();
            }
            else if (!string.IsNullOrEmpty(userId))
            {
                var user = await _userManager.FindByIdAsync(userId);
                if (user == null || string.IsNullOrEmpty(user.Email))
                {
                    TempData["Error"] = "Usuario no encontrado o no tiene email registrado.";
                    return RedirectToAction("Index");
                }
                recipients.Add((user.Email, user.FullName));
            }

            if (!recipients.Any())
            {
                TempData["Error"] = "No hay destinatarios con email válido.";
                return RedirectToAction("Index");
            }

            int sent = 0;
            foreach (var (email, name) in recipients)
            {
                try
                {
                    var htmlBody = $@"
                        <div style='font-family: Inter, Arial, sans-serif; max-width: 560px; margin: 0 auto; background: #1a1f2e; border-radius: 16px; padding: 40px; color: #ffffff;'>
                            <div style='text-align: center; margin-bottom: 30px;'>
                                <div style='display: inline-block; width: 48px; height: 48px; background: linear-gradient(135deg, #3b82f6, #6366f1); border-radius: 12px; line-height: 48px; font-size: 24px;'>✉</div>
                                <h2 style='color: #ffffff; margin-top: 16px;'>{subject}</h2>
                            </div>
                            <p style='color: rgba(255,255,255,0.7); font-size: 14px;'>Hola <strong style='color:#fff;'>{name}</strong>,</p>
                            <div style='background: rgba(255,255,255,0.06); border-radius: 12px; padding: 20px; margin: 20px 0; color: rgba(255,255,255,0.8); font-size: 14px; line-height: 1.7; white-space: pre-wrap;'>{message}</div>
                            <hr style='border: none; border-top: 1px solid rgba(255,255,255,0.08); margin: 24px 0;'>
                            <p style='color: rgba(255,255,255,0.3); font-size: 11px; text-align: center;'>© 2026 Plataforma Integral Hidrometeorológica • CFE</p>
                        </div>";
                    await _emailSender.SendEmailAsync(email, subject, htmlBody);
                    sent++;
                }
                catch { }
            }

            TempData["Success"] = sent == 1
                ? $"Notificación enviada a {recipients.First().Name}."
                : $"Notificación enviada a {sent} de {recipients.Count} usuarios.";

            return RedirectToAction("Index");
        }

        // POST: /Admin/ApproveUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveUser(string userId, string? role)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "Usuario no encontrado.";
                return RedirectToAction("Index");
            }

            if (!await CanManageUserAsync(user)) return Forbid();

            // Prevent non-SuperAdmin from approving with SuperAdmin role
            if (!IsSuperAdmin && role == "SuperAdmin") role = "Visualizador";

            user.IsApproved = true;
            user.ApprovedBy = User.Identity?.Name;
            user.ApprovedAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);

            // Cambiar rol si se especificó uno diferente
            if (!string.IsNullOrEmpty(role))
            {
                var currentRoles = await _userManager.GetRolesAsync(user);
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                await _userManager.AddToRoleAsync(user, role);
            }

            TempData["Success"] = $"Solicitud de '{user.FullName}' aprobada exitosamente.";

            // Notificar al usuario por email
            try
            {
                if (!string.IsNullOrEmpty(user.Email))
                {
                    var assignedRole = role ?? (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? "Visualizador";
                    var htmlBody = $@"
                        <div style='font-family: Inter, Arial, sans-serif; max-width: 500px; margin: 0 auto; background: #1a1f2e; border-radius: 16px; padding: 40px; color: #ffffff;'>
                            <div style='text-align: center; margin-bottom: 30px;'>
                                <div style='display: inline-block; width: 48px; height: 48px; background: linear-gradient(135deg, #10b981, #059669); border-radius: 12px; line-height: 48px; font-size: 24px;'>✓</div>
                                <h2 style='color: #ffffff; margin-top: 16px;'>Acceso Aprobado</h2>
                            </div>
                            <p style='color: rgba(255,255,255,0.7); font-size: 14px;'>Hola <strong style='color:#fff;'>{user.FullName}</strong>,</p>
                            <p style='color: rgba(255,255,255,0.7); font-size: 14px;'>Su solicitud de acceso a la Plataforma Integral Hidrometeorológica ha sido <strong style='color:#10b981;'>aprobada</strong>.</p>
                            <div style='background: rgba(255,255,255,0.06); border-radius: 12px; padding: 20px; margin: 20px 0;'>
                                <p style='color: rgba(255,255,255,0.6); font-size: 13px; margin: 8px 0;'><strong style='color:#fff;'>Usuario:</strong> {user.UserName}</p>
                                <p style='color: rgba(255,255,255,0.6); font-size: 13px; margin: 8px 0;'><strong style='color:#fff;'>Rol asignado:</strong> {assignedRole}</p>
                            </div>
                            <p style='color: rgba(255,255,255,0.5); font-size: 13px;'>Ya puede iniciar sesión con sus credenciales.</p>
                            <hr style='border: none; border-top: 1px solid rgba(255,255,255,0.08); margin: 24px 0;'>
                            <p style='color: rgba(255,255,255,0.3); font-size: 11px; text-align: center;'>© 2026 Plataforma Integral Hidrometeorológica • CFE</p>
                        </div>";
                    await _emailSender.SendEmailAsync(user.Email, "Acceso Aprobado - PIH", htmlBody);
                }
            }
            catch { }

            return RedirectToAction("Index");
        }

        // POST: /Admin/RejectUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectUser(string userId, string? reason)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "Usuario no encontrado.";
                return RedirectToAction("Index");
            }

            if (!await CanManageUserAsync(user)) return Forbid();

            var userName = user.FullName;

            // Notificar antes de eliminar
            try
            {
                if (!string.IsNullOrEmpty(user.Email))
                {
                    var htmlBody = $@"
                        <div style='font-family: Inter, Arial, sans-serif; max-width: 500px; margin: 0 auto; background: #1a1f2e; border-radius: 16px; padding: 40px; color: #ffffff;'>
                            <div style='text-align: center; margin-bottom: 30px;'>
                                <div style='display: inline-block; width: 48px; height: 48px; background: linear-gradient(135deg, #ef4444, #dc2626); border-radius: 12px; line-height: 48px; font-size: 24px;'>✗</div>
                                <h2 style='color: #ffffff; margin-top: 16px;'>Solicitud No Aprobada</h2>
                            </div>
                            <p style='color: rgba(255,255,255,0.7); font-size: 14px;'>Hola <strong style='color:#fff;'>{user.FullName}</strong>,</p>
                            <p style='color: rgba(255,255,255,0.7); font-size: 14px;'>Su solicitud de acceso a la Plataforma Integral Hidrometeorológica no fue aprobada.</p>
                            {(string.IsNullOrEmpty(reason) ? "" : $"<div style='background: rgba(255,255,255,0.06); border-radius: 12px; padding: 16px; margin: 20px 0;'><p style='color: rgba(255,255,255,0.6); font-size: 13px;'><strong style='color:#fff;'>Motivo:</strong> {reason}</p></div>")}
                            <p style='color: rgba(255,255,255,0.5); font-size: 13px;'>Si tiene preguntas, contacte al equipo de administración.</p>
                            <hr style='border: none; border-top: 1px solid rgba(255,255,255,0.08); margin: 24px 0;'>
                            <p style='color: rgba(255,255,255,0.3); font-size: 11px; text-align: center;'>© 2026 Plataforma Integral Hidrometeorológica • CFE</p>
                        </div>";
                    await _emailSender.SendEmailAsync(user.Email, "Solicitud No Aprobada - PIH", htmlBody);
                }
            }
            catch { }

            await _userManager.DeleteAsync(user);
            TempData["Success"] = $"Solicitud de '{userName}' rechazada y eliminada.";

            return RedirectToAction("Index");
        }

        // ─── Organismo CRUD (Dapper, solo SuperAdmin) ───

        [HttpGet]
        public async Task<IActionResult> Organismos()
        {
            if (!IsSuperAdmin) return Forbid();

            List<dynamic> organismos;
            using (var db = new SqlConnection(_sqlServerConn))
            {
                organismos = (await db.QueryAsync("SELECT Id, Nombre, Iniciales, Activo FROM Organismo ORDER BY Nombre")).ToList();
            }
            return Json(organismos);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveOrganismo(int id, string nombre, string iniciales)
        {
            if (!IsSuperAdmin) return Forbid();
            if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(iniciales))
                return Json(new { success = false, message = "Nombre e Iniciales son requeridos." });

            using var db = new SqlConnection(_sqlServerConn);
            if (id > 0)
            {
                await db.ExecuteAsync("UPDATE Organismo SET Nombre=@Nombre, Iniciales=@Iniciales WHERE Id=@Id",
                    new { Id = id, Nombre = nombre.Trim(), Iniciales = iniciales.Trim().ToUpper() });
            }
            else
            {
                await db.ExecuteAsync("INSERT INTO Organismo (Nombre, Iniciales, Activo) VALUES (@Nombre, @Iniciales, 1)",
                    new { Nombre = nombre.Trim(), Iniciales = iniciales.Trim().ToUpper() });
            }
            return Json(new { success = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleOrganismo(int id)
        {
            if (!IsSuperAdmin) return Forbid();
            using var db = new SqlConnection(_sqlServerConn);
            await db.ExecuteAsync("UPDATE Organismo SET Activo = CASE WHEN Activo = 1 THEN 0 ELSE 1 END WHERE Id=@Id", new { Id = id });
            return Json(new { success = true });
        }

        // ─── Centro de Trabajo CRUD (EF Core, solo SuperAdmin) ───

        [HttpGet]
        public async Task<IActionResult> CentrosTrabajo()
        {
            if (!IsSuperAdmin) return Forbid();
            var list = await _context.CentrosTrabajo.OrderBy(c => c.Nombre).ToListAsync();
            return Json(list);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveCentroTrabajo(int id, string nombre)
        {
            if (!IsSuperAdmin) return Forbid();
            if (string.IsNullOrWhiteSpace(nombre))
                return Json(new { success = false, message = "Nombre es requerido." });

            if (id > 0)
            {
                var ct = await _context.CentrosTrabajo.FindAsync(id);
                if (ct == null) return Json(new { success = false, message = "No encontrado." });
                ct.Nombre = nombre.Trim();
                await _context.SaveChangesAsync();
            }
            else
            {
                _context.CentrosTrabajo.Add(new CentroTrabajo { Nombre = nombre.Trim(), Activo = true });
                await _context.SaveChangesAsync();
            }
            return Json(new { success = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleCentroTrabajo(int id)
        {
            if (!IsSuperAdmin) return Forbid();
            var ct = await _context.CentrosTrabajo.FindAsync(id);
            if (ct == null) return Json(new { success = false });
            ct.Activo = !ct.Activo;
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // ─── Cambiar Organismo de usuario (SuperAdmin) ───

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeOrganismo(string userId, int? organismoId)
        {
            if (!IsSuperAdmin) return Forbid();
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return Json(new { success = false });
            user.OrganismoId = organismoId;
            await _userManager.UpdateAsync(user);
            return Json(new { success = true });
        }

        // ─── Cambiar Centro de Trabajo de usuario ───

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeCentroTrabajo(string userId, int? centroTrabajoId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return Json(new { success = false });
            // Non-SuperAdmin can only change users in their own organismo
            if (!IsSuperAdmin)
            {
                var me = await GetCurrentUserAsync();
                if (user.OrganismoId != me?.OrganismoId) return Forbid();
            }
            user.CentroTrabajoId = centroTrabajoId;
            await _userManager.UpdateAsync(user);
            return Json(new { success = true });
        }
    }
}
