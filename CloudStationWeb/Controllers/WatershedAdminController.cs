using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using CloudStationWeb.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CloudStationWeb.Controllers
{
    [Authorize(Roles = "SuperAdmin,Administrador")]
    public class WatershedAdminController : Controller
    {
        private readonly string? _sqlServerConn;
        private readonly string _kmlFolder;

        public WatershedAdminController(IConfiguration config, IWebHostEnvironment env)
        {
            _sqlServerConn = config.GetConnectionString("SqlServer");
            _kmlFolder = Path.Combine(env.WebRootPath, "kml");
        }

        // GET: WatershedAdmin
        public async Task<IActionResult> Index()
        {
            var model = new WatershedAdminViewModel();

            using (var db = new SqlConnection(_sqlServerConn))
            {
                model.Cuencas = (await db.QueryAsync<CuencaAdminDto>(@"
                    SELECT c.Id, c.Nombre, c.Codigo, c.ArchivoKml, c.Color, c.Activo, ISNULL(c.VerEnMapa, 0) AS VerEnMapa,
                           (SELECT COUNT(*) FROM Subcuenca sc WHERE sc.IdCuenca = c.Id) AS SubcuencaCount,
                           (SELECT COUNT(*) FROM Estacion e WHERE e.IdCuenca = c.Id) AS EstacionCount
                    FROM Cuenca c
                    ORDER BY c.Nombre")).ToList();

                model.EstacionesSinCuenca = (await db.QueryAsync<EstacionCuencaDto>(@"
                    SELECT Id, IdAsignado, Nombre, IdCuenca, IdSubcuenca, Activo
                    FROM Estacion
                    WHERE IdCuenca IS NULL
                    ORDER BY Nombre")).ToList();
            }

            return View(model);
        }

        // GET: WatershedAdmin/EditCuenca/{id}
        public async Task<IActionResult> EditCuenca(Guid id)
        {
            var model = new EditCuencaViewModel();

            using (var db = new SqlConnection(_sqlServerConn))
            {
                model.Cuenca = await db.QuerySingleOrDefaultAsync<CuencaAdminDto>(@"
                    SELECT c.Id, c.Nombre, c.Codigo, c.ArchivoKml, c.Color, c.Activo, ISNULL(c.VerEnMapa, 0) AS VerEnMapa,
                           (SELECT COUNT(*) FROM Subcuenca sc WHERE sc.IdCuenca = c.Id) AS SubcuencaCount,
                           (SELECT COUNT(*) FROM Estacion e WHERE e.IdCuenca = c.Id) AS EstacionCount
                    FROM Cuenca c WHERE c.Id = @Id", new { Id = id });

                if (model.Cuenca == null) return NotFound();

                model.Subcuencas = (await db.QueryAsync<SubcuencaAdminDto>(@"
                    SELECT sc.Id, sc.IdCuenca, sc.Nombre, sc.ArchivoKml, sc.Color, sc.Activo, ISNULL(sc.VerEnMapa, 0) AS VerEnMapa,
                           (SELECT COUNT(*) FROM Estacion e WHERE e.IdSubcuenca = sc.Id) AS EstacionCount
                    FROM Subcuenca sc
                    WHERE sc.IdCuenca = @Id
                    ORDER BY sc.Nombre", new { Id = id })).ToList();

                model.Estaciones = (await db.QueryAsync<EstacionCuencaDto>(@"
                    SELECT e.Id, e.IdAsignado, e.Nombre, e.IdCuenca, e.IdSubcuenca, e.Activo,
                           sc.Nombre AS SubcuencaNombre
                    FROM Estacion e
                    LEFT JOIN Subcuenca sc ON e.IdSubcuenca = sc.Id
                    WHERE e.IdCuenca = @Id
                    ORDER BY sc.Nombre, e.Nombre", new { Id = id })).ToList();
            }

            return View(model);
        }

        // POST: WatershedAdmin/SaveCuenca (AJAX)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveCuenca([FromBody] SaveCuencaRequest model)
        {
            if (string.IsNullOrWhiteSpace(model?.Nombre))
                return Json(new { success = false, message = "El nombre es requerido." });

            try
            {
                using (var db = new SqlConnection(_sqlServerConn))
                {
                    if (model.Id == null || model.Id == Guid.Empty)
                    {
                        var newId = Guid.NewGuid();
                        await db.ExecuteAsync(@"
                            INSERT INTO Cuenca (Id, Nombre, Codigo, Color, Activo, VerEnMapa)
                            VALUES (@Id, @Nombre, @Codigo, @Color, @Activo, @VerEnMapa)",
                            new { Id = newId, model.Nombre, model.Codigo, model.Color, model.Activo, model.VerEnMapa });
                        return Json(new { success = true, id = newId });
                    }
                    else
                    {
                        var sql = "UPDATE Cuenca SET Nombre = @Nombre, Activo = @Activo, VerEnMapa = @VerEnMapa";
                        if (model.Codigo != null) sql += ", Codigo = @Codigo";
                        if (model.Color != null) sql += ", Color = @Color";
                        sql += " WHERE Id = @Id";
                        await db.ExecuteAsync(sql,
                            new { model.Id, model.Nombre, model.Codigo, model.Color, model.Activo, model.VerEnMapa });
                        return Json(new { success = true, id = model.Id });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: WatershedAdmin/SaveSubcuenca (AJAX)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveSubcuenca([FromBody] SaveSubcuencaRequest model)
        {
            if (string.IsNullOrWhiteSpace(model?.Nombre))
                return Json(new { success = false, message = "El nombre es requerido." });

            if (model.IdCuenca == Guid.Empty)
                return Json(new { success = false, message = "La cuenca es requerida." });

            try
            {
                using (var db = new SqlConnection(_sqlServerConn))
                {
                    if (model.Id == null || model.Id == Guid.Empty)
                    {
                        var newId = Guid.NewGuid();
                        await db.ExecuteAsync(@"
                            INSERT INTO Subcuenca (Id, IdCuenca, Nombre, Color, Activo, VerEnMapa)
                            VALUES (@Id, @IdCuenca, @Nombre, @Color, @Activo, @VerEnMapa)",
                            new { Id = newId, model.IdCuenca, model.Nombre, model.Color, model.Activo, model.VerEnMapa });
                        return Json(new { success = true, id = newId });
                    }
                    else
                    {
                        await db.ExecuteAsync(@"
                            UPDATE Subcuenca SET Nombre = @Nombre, Activo = @Activo, IdCuenca = @IdCuenca, Color = @Color, VerEnMapa = @VerEnMapa
                            WHERE Id = @Id",
                            new { model.Id, model.Nombre, model.Activo, model.IdCuenca, model.Color, model.VerEnMapa });
                        return Json(new { success = true, id = model.Id });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: WatershedAdmin/DeleteSubcuenca (AJAX)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSubcuenca([FromBody] Guid id)
        {
            try
            {
                using (var db = new SqlConnection(_sqlServerConn))
                {
                    // First unlink any stations using this subcuenca
                    await db.ExecuteAsync(
                        "UPDATE Estacion SET IdSubcuenca = NULL WHERE IdSubcuenca = @Id",
                        new { Id = id });
                    await db.ExecuteAsync(
                        "DELETE FROM Subcuenca WHERE Id = @Id",
                        new { Id = id });
                }
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: WatershedAdmin/DeleteCuenca (AJAX)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCuenca([FromBody] Guid id)
        {
            try
            {
                using (var db = new SqlConnection(_sqlServerConn))
                {
                    // Unlink all stations from this cuenca
                    await db.ExecuteAsync(
                        "UPDATE Estacion SET IdCuenca = NULL, IdSubcuenca = NULL WHERE IdCuenca = @Id",
                        new { Id = id });
                    // Delete all subcuencas of this cuenca
                    await db.ExecuteAsync(
                        "DELETE FROM Subcuenca WHERE IdCuenca = @Id",
                        new { Id = id });
                    // Delete KML files
                    var kmlFile = await db.QuerySingleOrDefaultAsync<string>(
                        "SELECT ArchivoKml FROM Cuenca WHERE Id = @Id", new { Id = id });
                    if (!string.IsNullOrEmpty(kmlFile))
                    {
                        var fullPath = Path.Combine(_kmlFolder, kmlFile);
                        if (System.IO.File.Exists(fullPath))
                            System.IO.File.Delete(fullPath);
                    }
                    // Delete the cuenca
                    await db.ExecuteAsync(
                        "DELETE FROM Cuenca WHERE Id = @Id",
                        new { Id = id });
                }
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: WatershedAdmin/RemoveStationFromCuenca (AJAX)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveStationFromCuenca([FromBody] Guid id)
        {
            try
            {
                using (var db = new SqlConnection(_sqlServerConn))
                {
                    await db.ExecuteAsync(
                        "UPDATE Estacion SET IdCuenca = NULL, IdSubcuenca = NULL WHERE Id = @Id",
                        new { Id = id });
                }
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: WatershedAdmin/AssignStation (AJAX)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignStation([FromBody] AssignStationRequest model)
        {
            if (model == null || model.IdEstacion == Guid.Empty)
                return Json(new { success = false, message = "Estación inválida." });

            try
            {
                using (var db = new SqlConnection(_sqlServerConn))
                {
                    await db.ExecuteAsync(@"
                        UPDATE Estacion SET IdCuenca = @IdCuenca, IdSubcuenca = @IdSubcuenca
                        WHERE Id = @IdEstacion",
                        new { model.IdEstacion, model.IdCuenca, model.IdSubcuenca });
                }
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: WatershedAdmin/BulkAssign (AJAX)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkAssign([FromBody] BulkAssignRequest model)
        {
            if (model?.EstacionIds == null || model.EstacionIds.Count == 0)
                return Json(new { success = false, message = "Seleccione al menos una estación." });

            try
            {
                using (var db = new SqlConnection(_sqlServerConn))
                {
                    await db.ExecuteAsync(@"
                        UPDATE Estacion SET IdCuenca = @IdCuenca, IdSubcuenca = @IdSubcuenca
                        WHERE Id IN @Ids",
                        new { Ids = model.EstacionIds, model.IdCuenca, model.IdSubcuenca });
                }
                return Json(new { success = true, count = model.EstacionIds.Count });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: WatershedAdmin/GetSubcuencas/{cuencaId} (AJAX - for cascading dropdown)
        [HttpGet]
        public async Task<IActionResult> GetSubcuencas(Guid cuencaId)
        {
            using (var db = new SqlConnection(_sqlServerConn))
            {
                var items = await db.QueryAsync<CatalogItemGuid>(
                    "SELECT Id, Nombre FROM Subcuenca WHERE IdCuenca = @CuencaId AND Activo = 1 ORDER BY Nombre",
                    new { CuencaId = cuencaId });
                return Json(items);
            }
        }

        // GET: WatershedAdmin/GetUnassignedStations (AJAX)
        [HttpGet]
        public async Task<IActionResult> GetUnassignedStations()
        {
            using (var db = new SqlConnection(_sqlServerConn))
            {
                var stations = await db.QueryAsync<EstacionCuencaDto>(@"
                    SELECT Id, IdAsignado, Nombre, Activo
                    FROM Estacion
                    WHERE IdCuenca IS NULL
                    ORDER BY Nombre");
                return Json(stations);
            }
        }

        // POST: WatershedAdmin/UploadKml (File upload for cuenca or subcuenca)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadKml(Guid id, string tipo, Microsoft.AspNetCore.Http.IFormFile archivo)
        {
            if (archivo == null || archivo.Length == 0)
                return Json(new { success = false, message = "Seleccione un archivo KML." });

            var ext = Path.GetExtension(archivo.FileName).ToLowerInvariant();
            if (ext != ".kml" && ext != ".kmz")
                return Json(new { success = false, message = "Solo se permiten archivos .kml o .kmz" });

            if (archivo.Length > 10 * 1024 * 1024) // 10 MB limit
                return Json(new { success = false, message = "El archivo no puede exceder 10 MB." });

            try
            {
                // Generate safe filename
                var safeFileName = $"{tipo}_{id}{ext}";
                var filePath = Path.Combine(_kmlFolder, safeFileName);

                // Ensure directory exists
                Directory.CreateDirectory(_kmlFolder);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await archivo.CopyToAsync(stream);
                }

                // Update DB
                using (var db = new SqlConnection(_sqlServerConn))
                {
                    if (tipo == "cuenca")
                    {
                        await db.ExecuteAsync(
                            "UPDATE Cuenca SET ArchivoKml = @Kml WHERE Id = @Id",
                            new { Kml = safeFileName, Id = id });
                    }
                    else
                    {
                        await db.ExecuteAsync(
                            "UPDATE Subcuenca SET ArchivoKml = @Kml WHERE Id = @Id",
                            new { Kml = safeFileName, Id = id });
                    }
                }

                return Json(new { success = true, fileName = safeFileName });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: WatershedAdmin/RemoveKml (Remove KML association)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveKml([FromBody] RemoveKmlRequest model)
        {
            if (model == null || model.Id == Guid.Empty)
                return Json(new { success = false, message = "ID inválido." });

            try
            {
                using (var db = new SqlConnection(_sqlServerConn))
                {
                    string? currentFile;
                    if (model.Tipo == "cuenca")
                    {
                        currentFile = await db.QuerySingleOrDefaultAsync<string>(
                            "SELECT ArchivoKml FROM Cuenca WHERE Id = @Id", new { model.Id });
                        await db.ExecuteAsync(
                            "UPDATE Cuenca SET ArchivoKml = NULL WHERE Id = @Id", new { model.Id });
                    }
                    else
                    {
                        currentFile = await db.QuerySingleOrDefaultAsync<string>(
                            "SELECT ArchivoKml FROM Subcuenca WHERE Id = @Id", new { model.Id });
                        await db.ExecuteAsync(
                            "UPDATE Subcuenca SET ArchivoKml = NULL WHERE Id = @Id", new { model.Id });
                    }

                    // Delete physical file
                    if (!string.IsNullOrEmpty(currentFile))
                    {
                        var fullPath = Path.Combine(_kmlFolder, currentFile);
                        if (System.IO.File.Exists(fullPath))
                            System.IO.File.Delete(fullPath);
                    }
                }
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: WatershedAdmin/GetCuencasKmlFromDb (for Map - replaces appsettings)
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetCuencasKmlFromDb()
        {
            using (var db = new SqlConnection(_sqlServerConn))
            {
                var cuencas = await db.QueryAsync(@"
                    SELECT Id, Nombre, Codigo, ArchivoKml, Color
                    FROM Cuenca
                    WHERE Activo = 1 AND ISNULL(VerEnMapa, 0) = 1 AND ArchivoKml IS NOT NULL AND ArchivoKml != ''
                    ORDER BY Nombre");

                var subcuencas = await db.QueryAsync(@"
                    SELECT sc.Id, sc.Nombre, sc.ArchivoKml, sc.Color, c.Codigo AS CuencaCodigo
                    FROM Subcuenca sc
                    INNER JOIN Cuenca c ON sc.IdCuenca = c.Id
                    WHERE sc.Activo = 1 AND ISNULL(sc.VerEnMapa, 0) = 1 AND sc.ArchivoKml IS NOT NULL AND sc.ArchivoKml != ''
                    ORDER BY c.Nombre, sc.Nombre");

                return Json(new { cuencas, subcuencas });
            }
        }
    }
}
