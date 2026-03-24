using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using CloudStationWeb.Models;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using System;
using System.Linq;

namespace CloudStationWeb.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class StationAdminController : Controller
    {
        private readonly string? _sqlServerConn;

        public StationAdminController(IConfiguration config)
        {
            _sqlServerConn = config.GetConnectionString("SqlServer");
        }

        // GET: StationAdmin
        public async Task<IActionResult> Index()
        {
            using (var db = new SqlConnection(_sqlServerConn))
            {
                var stations = await db.QueryAsync<EstacionDto>(@"
                    SELECT Id, IdAsignado, Nombre, IdCuenca, IdSubcuenca, IdMunicipio, IdEntidadFederativa, 
                           Visible, Activo, GOES, GPRS, RADIO, Latitud, Longitud, IdOrganismo, Etiqueta, EsPresa
                    FROM Estacion
                    ORDER BY Nombre ASC");
                return View(stations);
            }
        }

        // GET: StationAdmin/Edit/5
        public async Task<IActionResult> Edit(Guid id)
        {
            var model = new StationAdminViewModel();

            using (var db = new SqlConnection(_sqlServerConn))
            {
                // 1. Get Core Station
                model.Estacion = await db.QuerySingleOrDefaultAsync<EstacionDto>(
                    "SELECT * FROM Estacion WHERE Id = @Id", new { Id = id });

                if (model.Estacion == null) return NotFound();

                // 2. Get Telemetry Settings
                model.GoesParams = await db.QuerySingleOrDefaultAsync<DatosGoesDto>(
                    "SELECT * FROM DatosGOES WHERE IdEstacion = @Id", new { Id = id });

                model.GprsParams = await db.QuerySingleOrDefaultAsync<DatosGprsDto>(
                    "SELECT * FROM DatosGPRS WHERE IdEstacion = @Id", new { Id = id });

                model.RadioParams = await db.QuerySingleOrDefaultAsync<DatosRadioDto>(
                    "SELECT * FROM DatosRADIO WHERE IdEstacion = @Id", new { Id = id });

                // 3. Get Sensors
                model.Sensores = (await db.QueryAsync<SensorAdminDto>(@"
                    SELECT s.*, 
                           ts.Nombre as TipoSensorNombre, ts.Icono, 
                           um.Nombre as UnidadMedidaNombre, um.Abreviatura as UnidadMedidaAbv
                    FROM Sensor s
                    INNER JOIN TipoSensor ts ON s.IdTipoSensor = ts.Id
                    LEFT JOIN UnidadMedida um ON s.IdUnidadMedida = um.Id
                    WHERE s.IdEstacion = @Id
                    ORDER BY s.Orden ASC", new { Id = id })).ToList();

                // 4. Get Bitacora Logs
                model.Bitacora = (await db.QueryAsync<BitacoraDto>(@"
                    SELECT Id, IdEstacion, FechaEvento, FechaRegistro, Descripcion, 
                           UsuarioNombreCompleto, Usuario, IdEstatus, IdEstatusEstacion
                    FROM BitacoraEstacion
                    WHERE IdEstacion = @Id
                    ORDER BY FechaEvento DESC", new { Id = id })).ToList();

                // 5. Get Thresholds (Umbrales)
                model.Umbrales = (await db.QueryAsync<UmbralDto>(@"
                    SELECT Id, IdSensor, ValorReferencia, Umbral, Operador, Nombre, Activo, Color, Periodo
                    FROM UmbralAlertas
                    WHERE IdSensor IN (SELECT Id FROM Sensor WHERE IdEstacion = @Id)
                    ORDER BY Nombre ASC", new { Id = id })).ToList();

                await LoadCatalogsAsync(model, db);
            }

            return View(model);
        }

        private async Task LoadCatalogsAsync(StationAdminViewModel model, SqlConnection db)
        {
            model.Catalogs.Cuencas = (await db.QueryAsync<CatalogItemGuid>(
                "SELECT Id, Nombre FROM Cuenca WHERE Activo = 1 ORDER BY Nombre")).ToList();
            model.Catalogs.Subcuencas = (await db.QueryAsync<CatalogItemGuid>(
                "SELECT Id, Nombre FROM Subcuenca WHERE Activo = 1 ORDER BY Nombre")).ToList();
            model.Catalogs.Entidades = (await db.QueryAsync<CatalogItemInt>(
                "SELECT Id, Nombre FROM EntidadFederativa ORDER BY Nombre")).ToList();
            model.Catalogs.Municipios = (await db.QueryAsync<CatalogItemInt>(
                "SELECT Id, Nombre FROM Municipio ORDER BY Nombre")).ToList();
            model.Catalogs.Organismos = (await db.QueryAsync<CatalogItemInt>(
                "SELECT Id, Nombre FROM Organismo WHERE Activo = 1 ORDER BY Nombre")).ToList();
            model.Catalogs.UnidadesMedida = (await db.QueryAsync<CatalogItemGuid>(
                "SELECT Id, Nombre FROM UnidadMedida ORDER BY Nombre")).ToList();
        }

        // POST: StationAdmin/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(StationAdminViewModel model)
        {
            if (!ModelState.IsValid)
            {
                using (var db = new SqlConnection(_sqlServerConn))
                {
                    await LoadCatalogsAsync(model, db);
                }
                return View(model);
            }

            using (var db = new SqlConnection(_sqlServerConn))
            {
                // Update Base Station
                await db.ExecuteAsync(@"
                    UPDATE Estacion SET 
                        Nombre = @Nombre, IdAsignado = @IdAsignado, Etiqueta = @Etiqueta,
                        Latitud = @Latitud, Longitud = @Longitud, Coordenadas = @Coordenadas,
                        IdCuenca = @IdCuenca, IdSubcuenca = @IdSubcuenca, 
                        IdEntidadFederativa = @IdEntidadFederativa, IdMunicipio = @IdMunicipio, IdOrganismo = @IdOrganismo,
                        Visible = @Visible, Activo = @Activo, EsPresa = @EsPresa,
                        GOES = @GOES, GPRS = @GPRS, RADIO = @RADIO
                    WHERE Id = @Id", model.Estacion);

                // --- UPDATE GOES ---
                if (model.Estacion.GOES && model.GoesParams != null)
                {
                    var exists = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM DatosGOES WHERE IdEstacion = @Id", new { Id = model.Estacion.Id });
                    if (exists > 0)
                    {
                        await db.ExecuteAsync(@"
                            UPDATE DatosGOES SET 
                                IdSatelital = @IdSatelital, Satelite = @Satelite, 
                                CanalPrimario = @CanalPrimario, CanalAlarma = @CanalAlarma,
                                Velocidad = @Velocidad, VelocidadAlarma = @VelocidadAlarma,
                                PrimeraTransmision = @PrimeraTransmision, VentanaTransmision = @VentanaTransmision,
                                RangoTransmision = @RangoTransmision, IntervaloTransmision = @IntervaloTransmision,
                                Decodificador = @Decodificador
                            WHERE IdEstacion = @IdEstacion", 
                            new { 
                                model.GoesParams.IdSatelital, model.GoesParams.Satelite, 
                                model.GoesParams.CanalPrimario, model.GoesParams.CanalAlarma,
                                model.GoesParams.Velocidad, model.GoesParams.VelocidadAlarma,
                                model.GoesParams.PrimeraTransmision, model.GoesParams.VentanaTransmision,
                                model.GoesParams.RangoTransmision, model.GoesParams.IntervaloTransmision,
                                model.GoesParams.Decodificador,
                                IdEstacion = model.Estacion.Id 
                            });
                    }
                    else
                    {
                        // Needs full insert object
                        await db.ExecuteAsync(@"
                            INSERT INTO DatosGOES (
                                IdEstacion, IdSatelital, Satelite, CanalPrimario, CanalAlarma, 
                                Velocidad, VelocidadAlarma, PrimeraTransmision, VentanaTransmision, 
                                RangoTransmision, IntervaloTransmision, Decodificador) 
                            VALUES (
                                @IdEstacion, @IdSatelital, @Satelite, @CanalPrimario, @CanalAlarma,
                                @Velocidad, @VelocidadAlarma, @PrimeraTransmision, @VentanaTransmision,
                                @RangoTransmision, @IntervaloTransmision, @Decodificador)", 
                            new { 
                                IdEstacion = model.Estacion.Id, model.GoesParams.IdSatelital, 
                                model.GoesParams.Satelite, model.GoesParams.CanalPrimario, model.GoesParams.CanalAlarma,
                                model.GoesParams.Velocidad, model.GoesParams.VelocidadAlarma,
                                model.GoesParams.PrimeraTransmision, model.GoesParams.VentanaTransmision,
                                model.GoesParams.RangoTransmision, model.GoesParams.IntervaloTransmision,
                                model.GoesParams.Decodificador
                            });
                    }
                }

                // --- UPDATE GPRS ---
                if (model.Estacion.GPRS && model.GprsParams != null)
                {
                    var exists = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM DatosGPRS WHERE IdEstacion = @Id", new { Id = model.Estacion.Id });
                    if (exists > 0)
                    {
                        await db.ExecuteAsync(@"
                            UPDATE DatosGPRS SET 
                                PeriodoTransmision = @PeriodoTransmision, Comentarios = @Comentarios,
                                UsuarioModificacion = 'Admin', FechaModificacion = @Now
                            WHERE IdEstacion = @IdEstacion", 
                            new { 
                                model.GprsParams.PeriodoTransmision, model.GprsParams.Comentarios,
                                Now = DateTime.Now, IdEstacion = model.Estacion.Id 
                            });
                    }
                    else
                    {
                        await db.ExecuteAsync(@"
                            INSERT INTO DatosGPRS (IdEstacion, PeriodoTransmision, Comentarios, FechaRegistro, UsuarioRegistro) 
                            VALUES (@IdEstacion, @PeriodoTransmision, ISNULL(@Comentarios,''), @Now, 'Admin')", 
                            new { 
                                IdEstacion = model.Estacion.Id, model.GprsParams.PeriodoTransmision, 
                                model.GprsParams.Comentarios, Now = DateTime.Now
                            });
                    }
                }

                // --- UPDATE RADIO ---
                if (model.Estacion.RADIO && model.RadioParams != null)
                {
                    var exists = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM DatosRADIO WHERE IdEstacion = @Id", new { Id = model.Estacion.Id });
                    if (exists > 0)
                    {
                        await db.ExecuteAsync(@"
                            UPDATE DatosRADIO SET 
                                PeriodoTransmision = @PeriodoTransmision, Comentarios = @Comentarios,
                                UsuarioModificacion = 'Admin', FechaModificacion = @Now
                            WHERE IdEstacion = @IdEstacion", 
                            new { 
                                model.RadioParams.PeriodoTransmision, model.RadioParams.Comentarios,
                                Now = DateTime.Now, IdEstacion = model.Estacion.Id 
                            });
                    }
                    else
                    {
                        await db.ExecuteAsync(@"
                            INSERT INTO DatosRADIO (IdEstacion, PeriodoTransmision, Comentarios, FechaRegistro, UsuarioRegistro) 
                            VALUES (@IdEstacion, @PeriodoTransmision, ISNULL(@Comentarios,''), @Now, 'Admin')", 
                            new { 
                                IdEstacion = model.Estacion.Id, model.RadioParams.PeriodoTransmision, 
                                model.RadioParams.Comentarios, Now = DateTime.Now
                            });
                    }
                }
            }

            TempData["SuccessMessage"] = "Estación actualizada correctamente.";
            return RedirectToAction(nameof(Index));
        }

        // POST: StationAdmin/UpdateSensor
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateSensor([FromBody] UpdateSensorRequest model)
        {
            if (model == null || model.Id == Guid.Empty)
                return Json(new { success = false, message = "Datos inválidos" });

            try
            {
                using (var db = new SqlConnection(_sqlServerConn))
                {
                    await db.ExecuteAsync(@"
                        UPDATE Sensor 
                        SET NumeroSensor = @NumeroSensor, 
                            ValorMinimo = @ValorMinimo, 
                            ValorMaximo = @ValorMaximo, 
                            PeriodoMuestra = @PeriodoMuestra,
                            TipoGrafica = @TipoGrafica,
                            Activo = @Activo,
                            IdUnidadMedida = @IdUnidadMedida,
                            FechaModificacion = @Now,
                            UsuarioModificacion = 'Admin' 
                        WHERE Id = @Id", 
                        new { 
                            model.NumeroSensor, 
                            model.ValorMinimo, 
                            model.ValorMaximo, 
                            model.PeriodoMuestra,
                            model.TipoGrafica,
                            model.Activo,
                            model.IdUnidadMedida, 
                            Now = DateTime.Now, 
                            model.Id 
                        });
                }
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // In production log exception
                return Json(new { success = false, message = "Error interno de servidor: " + ex.Message });
            }
        }

        // POST: StationAdmin/SaveUmbral
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveUmbral([FromBody] UmbralDto model)
        {
            if (model == null || model.IdSensor == Guid.Empty)
                return Json(new { success = false, message = "Datos inválidos" });

            try
            {
                using (var db = new SqlConnection(_sqlServerConn))
                {
                    if (model.Id > 0)
                    {
                        await db.ExecuteAsync(@"
                            UPDATE UmbralAlertas 
                            SET Umbral = @Umbral, 
                                ValorReferencia = @ValorReferencia,
                                Operador = @Operador, 
                                Nombre = @Nombre, 
                                Activo = @Activo,
                                Color = @Color,
                                Periodo = @Periodo
                            WHERE Id = @Id", model);
                    }
                    else
                    {
                        var newId = await db.ExecuteScalarAsync<long>(@"
                            INSERT INTO UmbralAlertas (IdSensor, Umbral, ValorReferencia, Operador, Nombre, Activo, Color, Periodo, IdUsuarioRegistra)
                            OUTPUT INSERTED.Id
                            VALUES (@IdSensor, @Umbral, @ValorReferencia, @Operador, @Nombre, @Activo, @Color, @Periodo, 'Admin')", 
                            model);
                        model.Id = newId;
                    }
                }
                return Json(new { success = true, umbral = model });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error interno: " + ex.Message });
            }
        }

        // POST: StationAdmin/DeleteUmbral
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUmbral(long id)
        {
            if (id <= 0) return Json(new { success = false, message = "ID inválido" });

            try
            {
                using (var db = new SqlConnection(_sqlServerConn))
                {
                    await db.ExecuteAsync("DELETE FROM UmbralAlertas WHERE Id = @Id", new { Id = id });
                }
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error interno: " + ex.Message });
            }
        }

        // GET: StationAdmin/GetCotas
        [HttpGet]
        public async Task<IActionResult> GetCotas(Guid sensorId)
        {
            if (sensorId == Guid.Empty) return Json(new { success = false });

            try
            {
                using (var db = new SqlConnection(_sqlServerConn))
                {
                    var cotas = await db.QueryAsync<CotaSensor>(
                        "SELECT * FROM CotaSensor WHERE IdSensor = @IdSensor ORDER BY FechaRegistro DESC", 
                        new { IdSensor = sensorId });
                    return Json(new { success = true, cotas = cotas });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: StationAdmin/SaveCota
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveCota([FromBody] CotaSensor cota)
        {
            if (cota == null || cota.IdSensor == Guid.Empty)
                return Json(new { success = false, message = "Datos inválidos" });

            try
            {
                var userName = User.Identity?.Name ?? "Admin";

                using (var db = new SqlConnection(_sqlServerConn))
                {
                    await db.OpenAsync();

                    if (cota.Id > 0)
                    {
                        // Actualizar
                        await db.ExecuteAsync(@"
                            UPDATE CotaSensor 
                            SET ValorCota = @ValorCota,
                                Operador = @Operador,
                                FechaInicio = @FechaInicio,
                                Fin = @Fin,
                                FechaFinal = @FechaFinal
                            WHERE Id = @Id",
                            new {
                                cota.ValorCota,
                                cota.Operador,
                                FechaInicio = cota.FechaInicio,
                                Fin = cota.Fin == true ? 1 : 0,
                                FechaFinal = cota.FechaFinal,
                                cota.Id
                            });
                    }
                    else
                    {
                        // Insertar nueva
                        await db.ExecuteAsync(@"
                            INSERT INTO CotaSensor 
                            (IdSensor, ValorCota, Operador, FechaInicio, Fin, FechaFinal, FechaRegistro, IdUsuarioRegistra, NombreCompleto)
                            VALUES 
                            (@IdSensor, @ValorCota, @Operador, @FechaInicio, @Fin, @FechaFinal, @FechaRegistro, @IdUsuarioRegistra, @NombreCompleto)",
                            new {
                                cota.IdSensor,
                                cota.ValorCota,
                                cota.Operador,
                                FechaInicio = cota.FechaInicio,
                                Fin = cota.Fin == true ? 1 : 0,
                                FechaFinal = cota.FechaFinal,
                                FechaRegistro = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                IdUsuarioRegistra = userName,
                                NombreCompleto = userName
                            });
                    }

                    // Actualizar Sensor.AplicaCota según cotas vigentes
                    await UpdateAplicaCotaAsync(db, cota.IdSensor);
                }
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al guardar cota: " + ex.Message });
            }
        }

        // POST: StationAdmin/DeleteCota
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCota(long id)
        {
            if (id <= 0) return Json(new { success = false, message = "ID inválido" });

            try
            {
                using (var db = new SqlConnection(_sqlServerConn))
                {
                    await db.OpenAsync();

                    // Obtener el IdSensor antes de borrar para actualizar AplicaCota
                    var sensorId = await db.QueryFirstOrDefaultAsync<Guid?>(
                        "SELECT IdSensor FROM CotaSensor WHERE Id = @Id", new { Id = id });

                    await db.ExecuteAsync("DELETE FROM CotaSensor WHERE Id = @Id", new { Id = id });

                    // Recalcular AplicaCota del sensor
                    if (sensorId.HasValue && sensorId.Value != Guid.Empty)
                        await UpdateAplicaCotaAsync(db, sensorId.Value);
                }
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al eliminar: " + ex.Message });
            }
        }

        /// <summary>
        /// Recalcula Sensor.AplicaCota = 1 si hay cotas vigentes, 0 si no.
        /// </summary>
        private async Task UpdateAplicaCotaAsync(SqlConnection db, Guid sensorId)
        {
            await db.ExecuteAsync(@"
                UPDATE Sensor 
                SET AplicaCota = CASE 
                    WHEN EXISTS (
                        SELECT 1 FROM CotaSensor 
                        WHERE IdSensor = @SensorId 
                          AND (Fin IS NULL OR Fin = 0)
                    ) THEN 1 ELSE 0 END
                WHERE Id = @SensorId",
                new { SensorId = sensorId });
        }
    }
}
