using System.Threading.Tasks;
using CloudStationWeb.Models;
using CloudStationWeb.Services;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace CloudStationWeb.Controllers
{
    [Authorize(AuthenticationSchemes = "Identity.Application," + Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)]
    public class MapController : Controller
    {
        private readonly DataService _dataService;
        private readonly IConfiguration _configuration;

        public MapController(DataService dataService, IConfiguration configuration)
        {
            _dataService = dataService;
            _configuration = configuration;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.Variables = await _dataService.GetAvailableVariablesAsync();
            return View();
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<JsonResult> GetVariables()
        {
            var vars = await _dataService.GetAvailableVariablesAsync();
            return Json(vars.Select(v => new { value = v.Value, label = v.Label }));
        }

        [HttpGet]
        public async Task<JsonResult> GetMapData(string variable = "precipitación", bool onlyCfe = true)
        {
            var data = await _dataService.GetMapDataAsync(variable, onlyCfe);
            return Json(data);
        }

        [HttpGet]
        public async Task<JsonResult> GetStationHistory(string stationId, string variable, int hours = 6)
        {
            var history = await _dataService.GetStationHistoryAsync(stationId, variable, hours);
            var rangeEnd = DateTime.Now;
            var rangeStart = rangeEnd.AddHours(-hours);
            var maintenanceIds = await _dataService.GetStationsInMaintenanceDuringAsync(rangeStart, rangeEnd);
            var isInMaint = maintenanceIds.Contains(stationId);
            return Json(new { data = history, enMantenimiento = isInMaint });
        }

        [HttpGet]
        public async Task<JsonResult> GetCuencaPrecipitacion(bool onlyCfe = true)
        {
            var data = await _dataService.GetCuencaSemaforoAsync(onlyCfe);
            return Json(data);
        }

        [HttpGet]
        public async Task<JsonResult> GetEventosLluvia(bool onlyCfe = true)
        {
            var data = await _dataService.GetEventosLluvia24hAsync(onlyCfe);
            return Json(data);
        }

        [HttpGet]
        public async Task<JsonResult> GetStationHyetograph(string stationId, int hours = 24)
        {
            var data = await _dataService.GetStationHyetographAsync(stationId, hours);
            var rangeEnd = DateTime.Now;
            var rangeStart = rangeEnd.AddHours(-hours);
            var maintenanceIds = await _dataService.GetStationsInMaintenanceDuringAsync(rangeStart, rangeEnd);
            var isInMaint = maintenanceIds.Contains(stationId);
            return Json(new { data = data, enMantenimiento = isInMaint });
        }

        [HttpGet]
        public async Task<JsonResult> GetCuencaEstaciones(string code, bool onlyCfe = true)
        {
            var data = await _dataService.GetCuencaEstacionesAsync(code, onlyCfe);
            return Json(data);
        }

        [HttpGet]
        public async Task<JsonResult> GetCuencasKml()
        {
            // Try DB first, fallback to appsettings
            var connStr = _configuration.GetConnectionString("SqlServer");
            try
            {
                using var db = new SqlConnection(connStr);
                var cuencas = (await db.QueryAsync(@"
                    SELECT Codigo AS code, Nombre AS label, ArchivoKml AS kmlFile, Color AS color
                    FROM Cuenca
                    WHERE Activo = 1 AND VerEnMapa = 1 AND ArchivoKml IS NOT NULL AND ArchivoKml != ''
                    ORDER BY Nombre")).ToList();

                if (cuencas.Count > 0)
                {
                    var subcuencas = (await db.QueryAsync(@"
                        SELECT sc.Nombre AS label, sc.ArchivoKml AS kmlFile, sc.Color AS color, c.Codigo AS parentCode
                        FROM Subcuenca sc
                        INNER JOIN Cuenca c ON sc.IdCuenca = c.Id
                        WHERE sc.Activo = 1 AND sc.VerEnMapa = 1 AND sc.ArchivoKml IS NOT NULL AND sc.ArchivoKml != ''
                        ORDER BY c.Nombre, sc.Nombre")).ToList();

                    return Json(new { cuencas, subcuencas });
                }
            }
            catch { /* fallback to config */ }

            // Fallback: appsettings.json
            var config = _configuration.GetSection("CuencasKml").Get<List<CuencaKmlConfig>>() ?? new();
            return Json(new {
                cuencas = config.Select(c => new { code = c.Code, label = c.Label, kmlFile = c.KmlFile, color = c.Color }),
                subcuencas = new object[0]
            });
        }

        [HttpGet]
        public async Task<JsonResult> GetStationBanner(string stationId)
        {
            if (string.IsNullOrEmpty(stationId))
                return Json(new { });

            var result = await _dataService.GetStationBannerAsync(stationId);
            return Json(result ?? new { });
        }
    }
}
