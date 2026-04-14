using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using CloudStationWeb.Models;
using CloudStationWeb.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace CloudStationWeb.Controllers
{
    [Authorize(Roles = "SuperAdmin,Administrador,Operador")]
    public class MaintenanceController : Controller
    {
        private readonly string? _sqlServerConn;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly string _uploadBasePath;
        private readonly IEmailSender _emailSender;
        private readonly PushNotificationService _pushService;
        private readonly ILogger<MaintenanceController> _logger;

        public MaintenanceController(
            IConfiguration config, 
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender,
            PushNotificationService pushService,
            ILogger<MaintenanceController> logger)
        {
            _sqlServerConn = config.GetConnectionString("SqlServer");
            _userManager = userManager;
            _emailSender = emailSender;
            _pushService = pushService;
            _logger = logger;
            _uploadBasePath = Path.Combine(Directory.GetCurrentDirectory(), "DocumentRepository", "mantenimiento");
        }

        // GET: /Maintenance
        public async Task<IActionResult> Index(string? estado, string? tipo, Guid? estacion, bool soloCfe = true)
        {
            var model = new MaintenanceIndexViewModel
            {
                FiltroEstado = estado,
                FiltroTipo = tipo,
                FiltroEstacion = estacion,
                FiltroSoloCfe = soloCfe
            };

            using (var db = new SqlConnection(_sqlServerConn))
            {
                // Estaciones para dropdown
                var estSql = soloCfe
                    ? @"SELECT e.Id, e.Nombre, e.IdAsignado FROM Estacion e
                        JOIN Organismo o ON e.IdOrganismo = o.Id
                        WHERE e.Activo = 1 AND o.Nombre = 'Comisión Federal de Electricidad'
                        ORDER BY e.Nombre"
                    : "SELECT Id, Nombre, IdAsignado FROM Estacion WHERE Activo = 1 ORDER BY Nombre";
                model.Estaciones = (await db.QueryAsync<EstacionSimpleDto>(estSql)).ToList();

                // Ordenes con filtros
                var sql = @"
                    SELECT o.*, e.Nombre AS NombreEstacion, e.IdAsignado,
                           (SELECT COUNT(*) FROM MantenimientoBitacora WHERE IdOrden = o.Id) AS TotalBitacoras,
                           (SELECT COUNT(*) FROM MantenimientoAdjunto WHERE IdOrden = o.Id) AS TotalAdjuntos
                    FROM MantenimientoOrden o
                    INNER JOIN Estacion e ON o.IdEstacion = e.Id
                    WHERE 1=1";

                var parameters = new DynamicParameters();

                if (soloCfe)
                {
                    sql += " AND e.IdOrganismo IN (SELECT Id FROM Organismo WHERE Nombre = N'Comisión Federal de Electricidad')";
                }

                if (!string.IsNullOrEmpty(estado))
                {
                    sql += " AND o.Estado = @Estado";
                    parameters.Add("Estado", estado);
                }
                if (!string.IsNullOrEmpty(tipo))
                {
                    sql += " AND o.TipoMantenimiento = @Tipo";
                    parameters.Add("Tipo", tipo);
                }
                if (estacion.HasValue && estacion.Value != Guid.Empty)
                {
                    sql += " AND o.IdEstacion = @IdEstacion";
                    parameters.Add("IdEstacion", estacion.Value);
                }

                sql += " ORDER BY CASE o.Estado WHEN 'En Proceso' THEN 1 WHEN 'Programado' THEN 2 WHEN 'Completado' THEN 3 WHEN 'Cancelado' THEN 4 END, o.FechaInicio DESC";

                model.Ordenes = (await db.QueryAsync<MantenimientoOrden>(sql, parameters)).ToList();

                // Estadísticas
                model.TotalActivas = model.Ordenes.Count(o => o.Estado == "En Proceso" || o.Estado == "Programado");
                model.TotalEstacionesAisladas = model.Ordenes.Count(o => o.AislarDatos && (o.Estado == "En Proceso" || o.Estado == "Programado"));
            }

            return View(model);
        }

        // GET: /Maintenance/Detail/5
        public async Task<IActionResult> Detail(long id)
        {
            var model = new MaintenanceDetailViewModel();

            using (var db = new SqlConnection(_sqlServerConn))
            {
                model.Orden = await db.QuerySingleOrDefaultAsync<MantenimientoOrden>(@"
                    SELECT o.*, e.Nombre AS NombreEstacion, e.IdAsignado
                    FROM MantenimientoOrden o
                    INNER JOIN Estacion e ON o.IdEstacion = e.Id
                    WHERE o.Id = @Id", new { Id = id });

                if (model.Orden == null) return NotFound();

                model.Bitacoras = (await db.QueryAsync<MantenimientoBitacora>(@"
                    SELECT * FROM MantenimientoBitacora 
                    WHERE IdOrden = @Id 
                    ORDER BY FechaEvento DESC", new { Id = id })).ToList();

                var allAdjuntos = (await db.QueryAsync<MantenimientoAdjunto>(@"
                    SELECT * FROM MantenimientoAdjunto 
                    WHERE IdOrden = @Id 
                    ORDER BY FechaSubido DESC", new { Id = id })).ToList();

                // Adjuntos generales (sin bitácora asociada)
                model.AdjuntosGenerales = allAdjuntos.Where(a => a.IdBitacora == null).ToList();

                // Adjuntos por bitácora
                foreach (var bit in model.Bitacoras)
                {
                    bit.Adjuntos = allAdjuntos.Where(a => a.IdBitacora == bit.Id).ToList();
                }
            }

            return View(model);
        }

        // POST: /Maintenance/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromBody] CreateMaintenanceRequest model)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null) return Unauthorized();

                using (var db = new SqlConnection(_sqlServerConn))
                {
                    var id = await db.ExecuteScalarAsync<long>(@"
                        INSERT INTO MantenimientoOrden 
                            (IdEstacion, TipoMantenimiento, Descripcion, FechaInicio, FechaFin, 
                             Estado, AislarDatos, Prioridad, ResponsableNombre, Observaciones,
                             CreadoPor, CreadoPorNombre, FechaCreacion)
                        OUTPUT INSERTED.Id
                        VALUES 
                            (@IdEstacion, @TipoMantenimiento, @Descripcion, @FechaInicio, @FechaFin,
                             'Programado', @AislarDatos, @Prioridad, @ResponsableNombre, @Observaciones,
                             @UserId, @UserName, GETDATE())",
                        new
                        {
                            model.IdEstacion,
                            model.TipoMantenimiento,
                            model.Descripcion,
                            model.FechaInicio,
                            model.FechaFin,
                            model.AislarDatos,
                            model.Prioridad,
                            model.ResponsableNombre,
                            model.Observaciones,
                            UserId = user.Id,
                            UserName = user.FullName ?? user.UserName
                        });

                    // Obtener nombre de estación para la notificación
                    var nombreEstacion = await db.QuerySingleOrDefaultAsync<string>(
                        "SELECT Nombre FROM Estacion WHERE Id = @Id", new { Id = model.IdEstacion }) ?? "Estación";

                    // Notificar admins en background (no bloquea response)
                    _ = NotifyAdminsAsync(
                        "nueva",
                        id,
                        nombreEstacion,
                        model.TipoMantenimiento ?? "General",
                        model.Prioridad ?? "Normal",
                        user.FullName ?? user.UserName ?? "Usuario",
                        model.Descripcion);

                    return Json(new { success = true, id });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /Maintenance/Update
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update([FromBody] UpdateMaintenanceRequest model)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null) return Unauthorized();

                using (var db = new SqlConnection(_sqlServerConn))
                {
                    await db.ExecuteAsync(@"
                        UPDATE MantenimientoOrden SET
                            TipoMantenimiento = @TipoMantenimiento,
                            Descripcion = @Descripcion,
                            FechaInicio = @FechaInicio,
                            FechaFin = @FechaFin,
                            Estado = @Estado,
                            AislarDatos = @AislarDatos,
                            Prioridad = @Prioridad,
                            ResponsableNombre = @ResponsableNombre,
                            Observaciones = @Observaciones,
                            ModificadoPor = @UserId,
                            ModificadoPorNombre = @UserName,
                            FechaModificacion = GETDATE()
                        WHERE Id = @Id",
                        new
                        {
                            model.Id,
                            model.TipoMantenimiento,
                            model.Descripcion,
                            model.FechaInicio,
                            model.FechaFin,
                            model.Estado,
                            model.AislarDatos,
                            model.Prioridad,
                            model.ResponsableNombre,
                            model.Observaciones,
                            UserId = user.Id,
                            UserName = user.FullName ?? user.UserName
                        });

                    return Json(new { success = true });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /Maintenance/UpdateEstado
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateEstado([FromBody] UpdateEstadoRequest model)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null) return Unauthorized();

                using (var db = new SqlConnection(_sqlServerConn))
                {
                    // Si se marca como Completado y no tiene FechaFin, poner ahora
                    if (model.Estado == "Completado")
                    {
                        await db.ExecuteAsync(@"
                            UPDATE MantenimientoOrden SET
                                Estado = @Estado,
                                FechaFin = CASE WHEN FechaFin IS NULL THEN GETDATE() ELSE FechaFin END,
                                ModificadoPor = @UserId,
                                ModificadoPorNombre = @UserName,
                                FechaModificacion = GETDATE()
                            WHERE Id = @Id",
                            new { model.Id, model.Estado, UserId = user.Id, UserName = user.FullName ?? user.UserName });

                        // Notificar admins que la orden fue completada
                        var orden = await db.QuerySingleOrDefaultAsync<dynamic>(@"
                            SELECT o.TipoMantenimiento, o.Prioridad, o.Descripcion, e.Nombre AS NombreEstacion
                            FROM MantenimientoOrden o
                            INNER JOIN Estacion e ON o.IdEstacion = e.Id
                            WHERE o.Id = @Id", new { model.Id });

                        if (orden != null)
                        {
                            _ = NotifyAdminsAsync(
                                "completada",
                                model.Id,
                                (string)(orden.NombreEstacion ?? "Estación"),
                                (string)(orden.TipoMantenimiento ?? "General"),
                                (string)(orden.Prioridad ?? "Normal"),
                                user.FullName ?? user.UserName ?? "Usuario",
                                (string?)orden.Descripcion);
                        }
                    }
                    else
                    {
                        await db.ExecuteAsync(@"
                            UPDATE MantenimientoOrden SET
                                Estado = @Estado,
                                ModificadoPor = @UserId,
                                ModificadoPorNombre = @UserName,
                                FechaModificacion = GETDATE()
                            WHERE Id = @Id",
                            new { model.Id, model.Estado, UserId = user.Id, UserName = user.FullName ?? user.UserName });
                    }

                    return Json(new { success = true });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /Maintenance/AddBitacora
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddBitacora([FromBody] AddBitacoraRequest model)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null) return Unauthorized();

                using (var db = new SqlConnection(_sqlServerConn))
                {
                    var id = await db.ExecuteScalarAsync<long>(@"
                        INSERT INTO MantenimientoBitacora 
                            (IdOrden, Descripcion, FechaEvento, FechaRegistro, Usuario, UsuarioNombre)
                        OUTPUT INSERTED.Id
                        VALUES 
                            (@IdOrden, @Descripcion, @FechaEvento, GETDATE(), @UserId, @UserName)",
                        new
                        {
                            model.IdOrden,
                            model.Descripcion,
                            model.FechaEvento,
                            UserId = user.Id,
                            UserName = user.FullName ?? user.UserName
                        });

                    return Json(new { success = true, id });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /Maintenance/UploadFile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadFile(long ordenId, long? bitacoraId, IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return Json(new { success = false, message = "Debe seleccionar un archivo." });

                // Límite de 50 MB
                if (file.Length > 50 * 1024 * 1024)
                    return Json(new { success = false, message = "El archivo no debe superar 50 MB." });

                // Validar extensiones permitidas
                var allowedExtensions = new[] { 
                    ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",  // Imágenes
                    ".mp4", ".avi", ".mov", ".wmv", ".mkv",            // Video
                    ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".xlsm", // Documentos
                    ".ppt", ".pptx", ".txt", ".csv",                   // Más docs
                    ".zip", ".rar", ".7z"                               // Comprimidos
                };
                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(ext))
                    return Json(new { success = false, message = $"Tipo de archivo no permitido: {ext}" });

                var user = await _userManager.GetUserAsync(User);
                if (user == null) return Unauthorized();

                // Crear directorio por orden
                var orderDir = Path.Combine(_uploadBasePath, ordenId.ToString());
                Directory.CreateDirectory(orderDir);

                // Nombre único para evitar colisiones
                var storedName = $"{Guid.NewGuid():N}{ext}";
                var fullPath = Path.Combine(orderDir, storedName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                using (var db = new SqlConnection(_sqlServerConn))
                {
                    var id = await db.ExecuteScalarAsync<long>(@"
                        INSERT INTO MantenimientoAdjunto 
                            (IdOrden, IdBitacora, NombreOriginal, NombreAlmacenado, RutaArchivo, 
                             TipoArchivo, TamanoBytes, SubidoPor, SubidoPorNombre, FechaSubido)
                        OUTPUT INSERTED.Id
                        VALUES 
                            (@IdOrden, @IdBitacora, @NombreOriginal, @NombreAlmacenado, @RutaArchivo,
                             @TipoArchivo, @TamanoBytes, @SubidoPor, @SubidoPorNombre, GETDATE())",
                        new
                        {
                            IdOrden = ordenId,
                            IdBitacora = bitacoraId,
                            NombreOriginal = file.FileName,
                            NombreAlmacenado = storedName,
                            RutaArchivo = Path.Combine("mantenimiento", ordenId.ToString(), storedName),
                            TipoArchivo = file.ContentType,
                            TamanoBytes = file.Length,
                            SubidoPor = user.Id,
                            SubidoPorNombre = user.FullName ?? user.UserName
                        });

                    return Json(new { success = true, id, nombreOriginal = file.FileName, tamano = file.Length });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: /Maintenance/DownloadFile/5
        public async Task<IActionResult> DownloadFile(long id)
        {
            using (var db = new SqlConnection(_sqlServerConn))
            {
                var adjunto = await db.QuerySingleOrDefaultAsync<MantenimientoAdjunto>(
                    "SELECT * FROM MantenimientoAdjunto WHERE Id = @Id", new { Id = id });

                if (adjunto == null) return NotFound();

                var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "DocumentRepository", adjunto.RutaArchivo!);
                if (!System.IO.File.Exists(fullPath)) return NotFound("Archivo no encontrado en disco.");

                var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
                return File(bytes, adjunto.TipoArchivo ?? "application/octet-stream", adjunto.NombreOriginal);
            }
        }

        // DELETE: /Maintenance/DeleteFile/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteFile([FromBody] DeleteFileRequest model)
        {
            try
            {
                using (var db = new SqlConnection(_sqlServerConn))
                {
                    var adjunto = await db.QuerySingleOrDefaultAsync<MantenimientoAdjunto>(
                        "SELECT * FROM MantenimientoAdjunto WHERE Id = @Id", new { Id = model.Id });

                    if (adjunto == null) return Json(new { success = false, message = "Archivo no encontrado." });

                    // Eliminar del disco
                    var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "DocumentRepository", adjunto.RutaArchivo!);
                    if (System.IO.File.Exists(fullPath))
                        System.IO.File.Delete(fullPath);

                    // Eliminar del BD
                    await db.ExecuteAsync("DELETE FROM MantenimientoAdjunto WHERE Id = @Id", new { model.Id });

                    return Json(new { success = true });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        #region Notificaciones

        private async Task NotifyAdminsAsync(
            string tipo, long ordenId, string nombreEstacion, 
            string tipoMantenimiento, string prioridad, string usuario, string? descripcion)
        {
            try
            {
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var detailUrl = $"{baseUrl}/Maintenance/Detail/{ordenId}";

                var esNueva = tipo == "nueva";
                var subject = esNueva
                    ? $"🔧 Nueva orden de mantenimiento — {nombreEstacion}"
                    : $"✅ Orden de mantenimiento completada — {nombreEstacion}";

                var prioridadColor = prioridad switch
                {
                    "Urgente" => "#db2828",
                    "Alta" => "#f2711c",
                    "Normal" => "#2185d0",
                    _ => "#6c757d"
                };

                var statusColor = esNueva ? "#2185d0" : "#21ba45";
                var statusText = esNueva ? "Programado" : "Completado";
                var statusIcon = esNueva ? "🔧" : "✅";

                var htmlBody = $@"
                    <p style='font-size:15px; color:#333;'>
                        {(esNueva ? $"Se ha registrado una <strong>nueva orden de mantenimiento</strong> por <strong>{System.Net.WebUtility.HtmlEncode(usuario)}</strong>." 
                                  : $"La orden de mantenimiento ha sido <strong>completada</strong> por <strong>{System.Net.WebUtility.HtmlEncode(usuario)}</strong>.")}
                    </p>
                    <table style='width:100%; border-collapse:collapse; margin:16px 0;'>
                        <tr>
                            <td style='padding:10px 14px; background:#f8f9fa; border:1px solid #e9ecef; font-weight:bold; width:140px; color:#555;'>Estación</td>
                            <td style='padding:10px 14px; border:1px solid #e9ecef;'>{System.Net.WebUtility.HtmlEncode(nombreEstacion)}</td>
                        </tr>
                        <tr>
                            <td style='padding:10px 14px; background:#f8f9fa; border:1px solid #e9ecef; font-weight:bold; color:#555;'>Tipo</td>
                            <td style='padding:10px 14px; border:1px solid #e9ecef;'>{System.Net.WebUtility.HtmlEncode(tipoMantenimiento)}</td>
                        </tr>
                        <tr>
                            <td style='padding:10px 14px; background:#f8f9fa; border:1px solid #e9ecef; font-weight:bold; color:#555;'>Prioridad</td>
                            <td style='padding:10px 14px; border:1px solid #e9ecef;'>
                                <span style='display:inline-block; padding:3px 10px; border-radius:12px; background:{prioridadColor}; color:white; font-size:12px; font-weight:bold;'>{System.Net.WebUtility.HtmlEncode(prioridad)}</span>
                            </td>
                        </tr>
                        <tr>
                            <td style='padding:10px 14px; background:#f8f9fa; border:1px solid #e9ecef; font-weight:bold; color:#555;'>Estado</td>
                            <td style='padding:10px 14px; border:1px solid #e9ecef;'>
                                <span style='display:inline-block; padding:3px 10px; border-radius:12px; background:{statusColor}; color:white; font-size:12px; font-weight:bold;'>{statusIcon} {statusText}</span>
                            </td>
                        </tr>
                        {(string.IsNullOrEmpty(descripcion) ? "" : $@"<tr>
                            <td style='padding:10px 14px; background:#f8f9fa; border:1px solid #e9ecef; font-weight:bold; color:#555;'>Descripción</td>
                            <td style='padding:10px 14px; border:1px solid #e9ecef;'>{System.Net.WebUtility.HtmlEncode(descripcion)}</td>
                        </tr>")}
                        <tr>
                            <td style='padding:10px 14px; background:#f8f9fa; border:1px solid #e9ecef; font-weight:bold; color:#555;'>{(esNueva ? "Creado por" : "Completado por")}</td>
                            <td style='padding:10px 14px; border:1px solid #e9ecef;'>{System.Net.WebUtility.HtmlEncode(usuario)}</td>
                        </tr>
                    </table>
                    <div style='text-align:center; margin:24px 0 8px;'>
                        <a href='{detailUrl}' style='display:inline-block; padding:12px 28px; background:#1a252f; color:white; text-decoration:none; border-radius:6px; font-weight:bold; font-size:14px;'>📋 Ver Orden de Mantenimiento</a>
                    </div>
                    <p style='font-size:12px; color:#999; text-align:center; margin-top:16px;'>Orden #{ordenId}</p>";

                var wrappedHtml = WrapHtmlTemplate(subject, htmlBody);

                // Obtener admins (SuperAdmin + Administrador)
                var admins = await _userManager.GetUsersInRoleAsync("SuperAdmin");
                var administradores = await _userManager.GetUsersInRoleAsync("Administrador");
                var adminEmails = admins.Concat(administradores)
                    .Where(u => !string.IsNullOrEmpty(u.Email) && u.IsActive)
                    .Select(u => new { u.Id, u.Email })
                    .DistinctBy(u => u.Email)
                    .ToList();

                // Enviar emails en paralelo
                var emailTasks = adminEmails.Select(a => _emailSender.SendEmailAsync(a.Email!, subject, wrappedHtml));
                await Task.WhenAll(emailTasks);

                _logger.LogInformation("Maintenance notification emails sent to {Count} admins for order {OrderId} ({Type})", 
                    adminEmails.Count, ordenId, tipo);

                // Push notifications
                if (_pushService.IsConfigured)
                {
                    var pushTitle = esNueva ? "🔧 Nueva orden de mantenimiento" : "✅ Mantenimiento completado";
                    var pushBody = $"{nombreEstacion} — {tipoMantenimiento} ({prioridad})";
                    var pushData = new Dictionary<string, string>
                    {
                        { "type", "maintenance" },
                        { "orderId", ordenId.ToString() },
                        { "action", tipo },
                        { "url", $"/Maintenance/Detail/{ordenId}" }
                    };

                    var pushTasks = adminEmails.Select(a => _pushService.SendToUserAsync(a.Id, pushTitle, pushBody, pushData));
                    await Task.WhenAll(pushTasks);

                    _logger.LogInformation("Maintenance push notifications sent for order {OrderId}", ordenId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send maintenance notifications for order {OrderId}", ordenId);
            }
        }

        private string WrapHtmlTemplate(string title, string bodyContent)
        {
            return $@"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='font-family: Arial, sans-serif; background:#f4f4f4; padding:20px;'>
  <div style='max-width:600px; margin:0 auto; background:white; border-radius:8px; overflow:hidden; box-shadow:0 2px 8px rgba(0,0,0,0.1);'>
    <div style='background:#1a252f; color:white; padding:20px; text-align:center;'>
      <h2 style='margin:0;'>🌊 Plataforma Integral Hidrometeorológica</h2>
      <p style='margin:4px 0 0; opacity:0.8; font-size:13px;'>Sistema Cuenca Grijalva</p>
    </div>
    <div style='padding:24px;'>
      <h3 style='color:#1a252f; margin-top:0;'>{title}</h3>
      {bodyContent}
    </div>
    <div style='background:#f8f8f8; padding:12px 24px; text-align:center; font-size:12px; color:#999;'>
      CFE — Subgerencia Regional de Generación Hidroeléctrica Grijalva
    </div>
  </div>
</body>
</html>";
        }

        #endregion

        // GET: /Maintenance/GetStationsInMaintenance
        [HttpGet]
        public async Task<IActionResult> GetStationsInMaintenance()
        {
            using (var db = new SqlConnection(_sqlServerConn))
            {
                var stations = await db.QueryAsync<dynamic>(@"
                    SELECT DISTINCT e.IdAsignado, e.Nombre, o.TipoMantenimiento, o.FechaInicio, o.FechaFin
                    FROM MantenimientoOrden o
                    INNER JOIN Estacion e ON o.IdEstacion = e.Id
                    WHERE o.AislarDatos = 1 
                    AND o.Estado IN ('En Proceso', 'Programado')
                    AND (o.FechaFin IS NULL OR o.FechaFin >= GETDATE())");

                return Json(stations);
            }
        }
    }

    // Small request DTO for estado updates
    public class UpdateEstadoRequest
    {
        public long Id { get; set; }
        public string Estado { get; set; } = "";
    }

    public class DeleteFileRequest
    {
        public long Id { get; set; }
    }
}
