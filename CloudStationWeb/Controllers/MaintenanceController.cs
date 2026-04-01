using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using CloudStationWeb.Models;
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

        public MaintenanceController(IConfiguration config, UserManager<ApplicationUser> userManager)
        {
            _sqlServerConn = config.GetConnectionString("SqlServer");
            _userManager = userManager;
            _uploadBasePath = Path.Combine(Directory.GetCurrentDirectory(), "DocumentRepository", "mantenimiento");
        }

        // GET: /Maintenance
        public async Task<IActionResult> Index(string? estado, string? tipo, Guid? estacion)
        {
            var model = new MaintenanceIndexViewModel
            {
                FiltroEstado = estado,
                FiltroTipo = tipo,
                FiltroEstacion = estacion
            };

            using (var db = new SqlConnection(_sqlServerConn))
            {
                // Estaciones para dropdown
                model.Estaciones = (await db.QueryAsync<EstacionSimpleDto>(
                    "SELECT Id, Nombre, IdAsignado FROM Estacion WHERE Activo = 1 ORDER BY Nombre")).ToList();

                // Ordenes con filtros
                var sql = @"
                    SELECT o.*, e.Nombre AS NombreEstacion, e.IdAsignado,
                           (SELECT COUNT(*) FROM MantenimientoBitacora WHERE IdOrden = o.Id) AS TotalBitacoras,
                           (SELECT COUNT(*) FROM MantenimientoAdjunto WHERE IdOrden = o.Id) AS TotalAdjuntos
                    FROM MantenimientoOrden o
                    INNER JOIN Estacion e ON o.IdEstacion = e.Id
                    WHERE 1=1";

                var parameters = new DynamicParameters();

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
