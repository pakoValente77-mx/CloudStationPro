using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CloudStationWeb.Data;
using CloudStationWeb.Models;
using CloudStationWeb.Services;

namespace CloudStationWeb.Controllers
{
    [Authorize]
    public class DocumentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly FunVasosService _funVasosService;

        public DocumentsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            FunVasosService funVasosService)
        {
            _context = context;
            _userManager = userManager;
            _funVasosService = funVasosService;
        }

        // GET: /Documents
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();
            var roles = await _userManager.GetRolesAsync(user);
            bool isAdmin = roles.Contains("Administrador");
            bool isOperador = roles.Contains("Operador");

            // Load products with permissions
            var productsQuery = _context.DocumentProducts
                .Where(p => p.IsActive);

            List<int> assignedIds = new();
            if (!isAdmin)
            {
                assignedIds = await _context.UserProductPermissions
                    .Where(up => up.UserId == user.Id)
                    .Select(up => up.ProductId)
                    .ToListAsync();

                productsQuery = productsQuery.Where(p => assignedIds.Contains(p.Id));
            }

            var products = await productsQuery
                .Include(p => p.Entries)
                    .ThenInclude(e => e.UploadedBy)
                .OrderBy(p => p.Name)
                .ToListAsync();

            var productIds = products.Select(p => p.Id).ToList();

            var today = DateTime.UtcNow.Date;
            var todayUploads = await _context.DocumentEntries
                .Where(e => productIds.Contains(e.ProductId) && e.UploadedAt.Date == today)
                .Select(e => e.ProductId)
                .Distinct()
                .ToListAsync();

            ViewData["IsAdmin"] = isAdmin;
            ViewData["IsOperador"] = isOperador;
            ViewData["TodayUploads"] = todayUploads;
            ViewData["Today"] = today;

            // Load logs
            var logsQuery = _context.DocumentAuditLogs.AsQueryable();
            if (!isAdmin)
            {
                logsQuery = logsQuery.Where(l => assignedIds.Contains(l.ProductId));
            }

            var logIds = await logsQuery
                .OrderByDescending(l => l.Timestamp)
                .Select(l => l.Id)
                .Take(10)
                .ToListAsync();

            var recentLogs = await _context.DocumentAuditLogs
                .Include(l => l.Product)
                .Where(l => logIds.Contains(l.Id))
                .OrderByDescending(l => l.Timestamp)
                .ToListAsync();

            ViewData["RecentLogs"] = recentLogs;

            return View(products);
        }

        // POST: /Documents/Upload
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,Administrador,Operador")]
        public async Task<IActionResult> Upload(int productId, IFormFile file, string? notes)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Debe seleccionar un archivo.";
                return RedirectToAction("Index");
            }

            var product = await _context.DocumentProducts.FindAsync(productId);
            if (product == null || !product.IsActive)
            {
                TempData["Error"] = "Producto no válido.";
                return RedirectToAction("Index");
            }

            // Validate file type (Excel only)
            var allowedTypes = new[] { ".xlsx", ".xls", ".xlsm" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedTypes.Contains(ext))
            {
                TempData["Error"] = "Solo se permiten archivos Excel (.xlsx, .xls, .xlsm).";
                return RedirectToAction("Index");
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (!User.IsInRole("Administrador"))
            {
                var hasPermission = await _context.UserProductPermissions
                    .AnyAsync(p => p.UserId == user.Id && p.ProductId == productId);
                if (!hasPermission)
                {
                    TempData["Error"] = "No tiene permisos para subir archivos a este reporte.";
                    return RedirectToAction("Index");
                }
            }

            var now = DateTime.UtcNow;

            // Validate storage path configured
            if (string.IsNullOrEmpty(product.StoragePath))
            {
                TempData["Error"] = $"El producto '{product.Name}' no tiene ruta de almacenamiento configurada. Configure la ruta en Almacenamiento.";
                return RedirectToAction("Index");
            }

            // Generate filename: {FilePrefix}{DDMMYY}.xlsx
            var prefix = string.IsNullOrEmpty(product.FilePrefix) ? product.Code.ToUpper() : product.FilePrefix;
            var datePart = now.ToString("ddMMyy");

            // --- For "vasos" product: detect date from Excel content ---
            DateTime? excelDate = null;
            string? tempFilePath = null;
            if (product.Code == "vasos")
            {
                // Save to temp file first to read the date
                tempFilePath = Path.Combine(Path.GetTempPath(), $"fv_temp_{Guid.NewGuid()}{ext}");
                using (var tempStream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await file.CopyToAsync(tempStream);
                }

                // Extract date from Excel content
                excelDate = _funVasosService.ExtractDateFromFile(tempFilePath);

                // Also try from filename
                var filenameDate = FunVasosService.ExtractDateFromFilename(file.FileName);

                if (excelDate.HasValue)
                {
                    // Validate: if filename has date, it should match Excel content
                    if (filenameDate.HasValue && filenameDate.Value.Date != excelDate.Value.Date)
                    {
                        TempData["Warning"] = $"La fecha del nombre del archivo ({filenameDate.Value:dd/MM/yyyy}) no coincide con la fecha dentro del Excel ({excelDate.Value:dd/MM/yyyy}). Se usó la fecha del contenido: {excelDate.Value:dd/MM/yyyy}.";
                    }

                    datePart = excelDate.Value.ToString("ddMMyy");
                }
                else if (filenameDate.HasValue)
                {
                    excelDate = filenameDate;
                    datePart = filenameDate.Value.ToString("ddMMyy");
                }
            }

            var storedFileName = $"{prefix}{datePart}{ext}";

            // Create directory (flat per product, organized by storage path)
            Directory.CreateDirectory(product.StoragePath);

            var storedPath = storedFileName;
            var fullPath = Path.Combine(product.StoragePath, storedFileName);

            // Check if there's already a file for this date — replace it
            // For "vasos", use the Excel date; for others, use today
            var targetDate = (product.Code == "vasos" && excelDate.HasValue) ? excelDate.Value.Date : now.Date;
            var targetDateEnd = targetDate.AddDays(1);
            var existingToday = await _context.DocumentEntries
                .Where(e => e.ProductId == productId
                    && e.UploadedAt >= targetDate
                    && e.UploadedAt < targetDateEnd)
                .ToListAsync();

            var isReplace = existingToday.Any();

            // Delete old physical files for today
            foreach (var old in existingToday)
            {
                var oldPath = Path.Combine(product.StoragePath, old.StoredPath);
                if (System.IO.File.Exists(oldPath))
                    System.IO.File.Delete(oldPath);
                old.IsLatest = false;
            }

            // Also mark any other "latest" entries as not latest
            var otherLatest = await _context.DocumentEntries
                .Where(e => e.ProductId == productId && e.IsLatest)
                .ToListAsync();
            foreach (var prev in otherLatest)
            {
                prev.IsLatest = false;
            }

            // Save file to disk
            if (tempFilePath != null && System.IO.File.Exists(tempFilePath))
            {
                // For "vasos": move temp file to final destination
                System.IO.File.Copy(tempFilePath, fullPath, overwrite: true);
                System.IO.File.Delete(tempFilePath);
            }
            else
            {
                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
            }

            // Create new entry
            var entry = new DocumentEntry
            {
                ProductId = productId,
                FileName = file.FileName,
                StoredFileName = storedFileName,
                StoredPath = storedPath,
                FileSize = file.Length,
                ContentType = file.ContentType,
                UploadedById = user.Id,
                UploadedAt = (product.Code == "vasos" && excelDate.HasValue) ? excelDate.Value : now,
                IsLatest = true,
                Notes = (product.Code == "vasos" && excelDate.HasValue)
                    ? $"Fecha del reporte: {excelDate.Value:dd/MM/yyyy}" + (string.IsNullOrEmpty(notes) ? "" : $" | {notes}")
                    : notes
            };

            _context.DocumentEntries.Add(entry);
            await _context.SaveChangesAsync();

            // Audit log
            _context.DocumentAuditLogs.Add(new DocumentAuditLog
            {
                EntryId = entry.Id,
                ProductId = productId,
                Action = isReplace ? "Replace" : "Upload",
                UserId = user.Id,
                UserName = user.FullName,
                Timestamp = now,
                Details = $"{(isReplace ? "Reemplazó" : "Subió")} '{file.FileName}' → {storedFileName} ({FormatFileSize(file.Length)})"
            });
            await _context.SaveChangesAsync();

            // --- Auto-parse Funcionamiento de Vasos ---
            if (product.Code == "vasos")
            {
                try
                {
                    var (rowsInserted, reportDate, parseErrors) = await _funVasosService.ParseAndStoreAsync(fullPath);
                    if (parseErrors.Any())
                    {
                        TempData["Warning"] = $"Archivo subido como {storedFileName}, pero con errores al procesar datos: {string.Join("; ", parseErrors)}";
                    }
                    else
                    {
                        TempData["Success"] = $"Archivo subido como {storedFileName}. Se vaciaron {rowsInserted} registros horarios ({reportDate:dd/MM/yyyy}) a la base de datos.";
                    }
                }
                catch (Exception ex)
                {
                    TempData["Warning"] = $"Archivo subido como {storedFileName}, pero falló el vaciado de datos: {ex.Message}";
                }
                return RedirectToAction("Index");
            }

            TempData["Success"] = $"Archivo subido exitosamente como {storedFileName}.";
            return RedirectToAction("Index");
        }

        // POST: /Documents/UploadHistorical
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,Administrador,Operador")]
        public async Task<IActionResult> UploadHistorical(int productId, IFormFile file, DateTime reportDate)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Debe seleccionar un archivo.";
                return RedirectToAction("History", new { id = productId });
            }

            var product = await _context.DocumentProducts.FindAsync(productId);
            if (product == null || !product.IsActive)
            {
                TempData["Error"] = "Producto no válido.";
                return RedirectToAction("History", new { id = productId });
            }

            // Validate file type (Excel only)
            var allowedTypes = new[] { ".xlsx", ".xls", ".xlsm" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedTypes.Contains(ext))
            {
                TempData["Error"] = "Solo se permiten archivos Excel (.xlsx, .xls, .xlsm).";
                return RedirectToAction("History", new { id = productId });
            }

            // Validate reportDate is not in the future
            if (reportDate.Date > DateTime.UtcNow.Date)
            {
                TempData["Error"] = "La fecha del reporte no puede ser futura.";
                return RedirectToAction("History", new { id = productId });
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                TempData["Error"] = "Usuario no autenticado.";
                return RedirectToAction("History", new { id = productId });
            }

            if (!User.IsInRole("Administrador"))
            {
                var hasPermission = await _context.UserProductPermissions
                    .AnyAsync(p => p.UserId == user.Id && p.ProductId == productId);
                if (!hasPermission)
                {
                    TempData["Error"] = "No tiene permisos para subir archivos históricos a este reporte.";
                    return RedirectToAction("History", new { id = productId });
                }
            }

            // Check if storage path is configured
            if (string.IsNullOrEmpty(product.StoragePath))
            {
                TempData["Error"] = $"La ruta de almacenamiento para '{product.Name}' no está configurada.";
                return RedirectToAction("History", new { id = productId });
            }

            // Create folder structure: YYYY/MM/
            var folderRelative = Path.Combine(reportDate.Year.ToString(), reportDate.Month.ToString("D2"));
            var folderFull = Path.Combine(product.StoragePath, folderRelative);
            Directory.CreateDirectory(folderFull);

            // Generate stored filename using reportDate: {Prefix}{DDMMYY}.xlsx
            var storedFileName = $"{product.FilePrefix}{reportDate:ddMMyy}{ext}";
            var storedPath = Path.Combine(folderRelative, storedFileName);
            storedPath = storedPath.Replace("\\", "/");

            var fullPath = Path.Combine(product.StoragePath, storedPath);

            // Check if file already exists for this date
            var existingEntry = await _context.DocumentEntries
                .Where(e => e.ProductId == productId && e.UploadedAt.Date == reportDate.Date)
                .FirstOrDefaultAsync();

            bool isReplace = existingEntry != null;

            if (isReplace)
            {
                // Delete old physical file
                var oldPath = Path.Combine(product.StoragePath, existingEntry!.StoredPath);
                if (System.IO.File.Exists(oldPath))
                    System.IO.File.Delete(oldPath);

                // Remove old entry
                _context.DocumentEntries.Remove(existingEntry);
            }

            // Save file
            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Create entry with reportDate as UploadedAt
            var entry = new DocumentEntry
            {
                ProductId = productId,
                FileName = file.FileName,
                StoredFileName = storedFileName,
                StoredPath = storedPath,
                FileSize = file.Length,
                ContentType = file.ContentType,
                UploadedById = user.Id,
                UploadedAt = reportDate, // Use custom date
                IsLatest = false, // Historical uploads are not marked as latest
                Notes = $"Carga histórica - Fecha del reporte: {reportDate:dd/MM/yyyy}"
            };

            _context.DocumentEntries.Add(entry);
            await _context.SaveChangesAsync();

            // Audit log
            _context.DocumentAuditLogs.Add(new DocumentAuditLog
            {
                EntryId = entry.Id,
                ProductId = productId,
                Action = isReplace ? "Replace Historical" : "Upload Historical",
                UserId = user.Id,
                UserName = user.FullName,
                Timestamp = DateTime.UtcNow,
                Details = $"{(isReplace ? "Reemplazó" : "Subió")} '{entry.StoredFileName}' con fecha {reportDate:dd/MM/yyyy}"
            });
            await _context.SaveChangesAsync();

            // --- Auto-parse Funcionamiento de Vasos (historical) ---
            if (product.Code == "vasos")
            {
                try
                {
                    var (rowsInserted, parsedDate, parseErrors) = await _funVasosService.ParseAndStoreAsync(fullPath);
                    if (parseErrors.Any())
                    {
                        TempData["Warning"] = $"Archivo histórico subido como {entry.StoredFileName}, pero con errores: {string.Join("; ", parseErrors)}";
                    }
                    else
                    {
                        TempData["Success"] = $"Reporte histórico '{entry.StoredFileName}' subido. Se vaciaron {rowsInserted} registros ({parsedDate:dd/MM/yyyy}) a la base de datos.";
                    }
                }
                catch (Exception ex)
                {
                    TempData["Warning"] = $"Archivo histórico subido, pero falló el vaciado: {ex.Message}";
                }
                return RedirectToAction("History", new { id = productId, year = reportDate.Year, month = reportDate.Month });
            }

            TempData["Success"] = $"Reporte histórico '{entry.StoredFileName}' subido correctamente para {reportDate:dd/MM/yyyy}.";
            return RedirectToAction("History", new { id = productId, year = reportDate.Year, month = reportDate.Month });
        }

        // GET: /Documents/Download/5
        public async Task<IActionResult> Download(int id)
        {
            var entry = await _context.DocumentEntries
                .Include(e => e.Product)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (entry == null) return NotFound();

            // Permissions check
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();
            if (!User.IsInRole("Administrador"))
            {
                var hasPermission = await _context.UserProductPermissions
                    .AnyAsync(p => p.UserId == user.Id && p.ProductId == entry.ProductId);
                if (!hasPermission) return Forbid();
            }

            var fullPath = Path.Combine(entry.Product.StoragePath ?? "", entry.StoredPath);
            if (!System.IO.File.Exists(fullPath)) return NotFound("Archivo no encontrado en el servidor.");

            // Audit log
            if (user != null)
            {
                _context.DocumentAuditLogs.Add(new DocumentAuditLog
                {
                    EntryId = entry.Id,
                    ProductId = entry.ProductId,
                    Action = "Download",
                    UserId = user.Id,
                    UserName = user.FullName,
                    Timestamp = DateTime.UtcNow,
                    Details = $"Descargó '{entry.FileName}'"
                });
                await _context.SaveChangesAsync();
            }

            return PhysicalFile(fullPath, entry.ContentType, entry.StoredFileName);
        }



        // GET: /Documents/History/5
        public async Task<IActionResult> History(int id, int? year, int? month)
        {
            var product = await _context.DocumentProducts
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null) return NotFound();

            // Permissions check
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();
            bool isAdmin = User.IsInRole("Administrador");
            if (!isAdmin)
            {
                var hasPermission = await _context.UserProductPermissions
                    .AnyAsync(p => p.UserId == user.Id && p.ProductId == id);
                if (!hasPermission) return Forbid();
            }

            var query = _context.DocumentEntries
                .Where(e => e.ProductId == id)
                .Include(e => e.UploadedBy)
                .AsQueryable();

            if (year.HasValue)
                query = query.Where(e => e.UploadedAt.Year == year.Value);
            if (month.HasValue)
                query = query.Where(e => e.UploadedAt.Month == month.Value);

            var entries = await query
                .OrderByDescending(e => e.UploadedAt)
                .ToListAsync();

            // Get available years for filter
            var availableYears = await _context.DocumentEntries
                .Where(e => e.ProductId == id)
                .Select(e => e.UploadedAt.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .ToListAsync();

            var roles = await _userManager.GetRolesAsync(user);
            ViewData["Product"] = product;
            ViewData["IsAdmin"] = roles.Contains("Administrador");
            ViewData["IsOperador"] = roles.Contains("Operador");
            ViewData["SelectedYear"] = year;
            ViewData["SelectedMonth"] = month;
            ViewData["AvailableYears"] = availableYears;
            return View(entries);
        }

        // GET: /Documents/AuditLog
        [Authorize(Roles = "SuperAdmin,Administrador")]
        public async Task<IActionResult> AuditLog(int? productId, string? action, int page = 1)
        {
            var query = _context.DocumentAuditLogs
                .Include(l => l.Product)
                .AsQueryable();

            if (productId.HasValue)
                query = query.Where(l => l.ProductId == productId.Value);

            if (!string.IsNullOrEmpty(action))
                query = query.Where(l => l.Action == action);

            var totalItems = await query.CountAsync();
            var pageSize = 25;
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            var logs = await query
                .OrderByDescending(l => l.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var products = await _context.DocumentProducts.OrderBy(p => p.Name).ToListAsync();

            ViewData["Products"] = products;
            ViewData["SelectedProductId"] = productId;
            ViewData["SelectedAction"] = action;
            ViewData["CurrentPage"] = page;
            ViewData["TotalPages"] = totalPages;

            return View(logs);
        }

        // GET: /Documents/ManageProducts (Admin)
        [Authorize(Roles = "SuperAdmin,Administrador")]
        public async Task<IActionResult> ManageProducts()
        {
            var products = await _context.DocumentProducts
                .OrderBy(p => p.Name)
                .ToListAsync();
            return View(products);
        }

        // POST: /Documents/CreateProduct
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,Administrador")]
        public async Task<IActionResult> CreateProduct(string name, string code, string? description, bool requiredDaily)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(code))
            {
                TempData["Error"] = "Nombre y código son requeridos.";
                return RedirectToAction("ManageProducts");
            }

            code = code.ToLowerInvariant().Replace(" ", "_");

            if (await _context.DocumentProducts.AnyAsync(p => p.Code == code))
            {
                TempData["Error"] = $"Ya existe un producto con el código '{code}'.";
                return RedirectToAction("ManageProducts");
            }

            _context.DocumentProducts.Add(new DocumentProduct
            {
                Name = name,
                Code = code,
                Description = description,
                IsActive = true,
                RequiredDaily = requiredDaily,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Producto '{name}' creado exitosamente.";
            return RedirectToAction("ManageProducts");
        }

        // POST: /Documents/ToggleProduct/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,Administrador")]
        public async Task<IActionResult> ToggleProduct(int id)
        {
            var product = await _context.DocumentProducts.FindAsync(id);
            if (product == null) return NotFound();

            product.IsActive = !product.IsActive;
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Producto '{product.Name}' {(product.IsActive ? "activado" : "desactivado")}.";
            return RedirectToAction("ManageProducts");
        }

        // GET: /Documents/StorageSettings (Admin)
        [Authorize(Roles = "SuperAdmin,Administrador")]
        public async Task<IActionResult> StorageSettings()
        {
            var products = await _context.DocumentProducts
                .OrderBy(p => p.Name)
                .ToListAsync();
            return View(products);
        }

        // POST: /Documents/UpdateStoragePath
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,Administrador")]
        public async Task<IActionResult> UpdateStoragePath(int id, string storagePath)
        {
            var product = await _context.DocumentProducts.FindAsync(id);
            if (product == null) return NotFound();

            // Normalize path
            storagePath = storagePath?.Trim() ?? "";

            if (!string.IsNullOrEmpty(storagePath))
            {
                try
                {
                    Directory.CreateDirectory(storagePath);
                    product.StoragePath = storagePath;
                    await _context.SaveChangesAsync();
                    TempData["Success"] = $"Ruta de '{product.Name}' actualizada a: {storagePath}";
                }
                catch (Exception ex)
                {
                    TempData["Error"] = $"Error al crear el directorio: {ex.Message}";
                }
            }
            else
            {
                TempData["Error"] = "La ruta no puede estar vacía.";
            }

            return RedirectToAction("StorageSettings");
        }

        // API: /Documents/GetPendingAlerts
        [HttpGet]
        public async Task<IActionResult> GetPendingAlerts()
        {
            var today = DateTime.UtcNow.Date;
            var requiredProducts = await _context.DocumentProducts
                .Where(p => p.IsActive && p.RequiredDaily)
                .ToListAsync();

            var todayUploads = await _context.DocumentEntries
                .Where(e => e.UploadedAt.Date == today)
                .Select(e => e.ProductId)
                .Distinct()
                .ToListAsync();

            var pending = requiredProducts
                .Where(p => !todayUploads.Contains(p.Id))
                .Select(p => new { p.Id, p.Name, p.Code })
                .ToList();

            return Json(new { count = pending.Count, products = pending });
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }
    }
}
