using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using CloudStationWeb.Services;
using CloudStationWeb.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CloudStationWeb.Controllers
{
    [Authorize(AuthenticationSchemes = "Identity.Application," + JwtBearerDefaults.AuthenticationScheme)]
    public class DataAnalysisController : Controller
    {
        private readonly DataService _dataService;
        private readonly string _sqlServerConn;

        public DataAnalysisController(DataService dataService, IConfiguration config)
        {
            _dataService = dataService;
            _sqlServerConn = config.GetConnectionString("SqlServer") ?? "";
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetStations(bool onlyCfe = true)
        {
            try
            {
                var stations = await _dataService.GetStationListAsync(onlyCfe);
                return Json(stations);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> GetAnalysisData([FromBody] DataAnalysisRequest request)
        {
            try
            {
                var response = await _dataService.GetDataAnalysisAsync(request);
                return Json(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetStationVariables(string stationId)
        {
            try
            {
                var variables = await _dataService.GetStationVariablesAsync(stationId);
                return Json(variables);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Devuelve la cota activa (sin fecha final definida) del sensor dado.
        /// También devuelve el historial completo para mostrar en el modal.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetActiveCota(Guid sensorId)
        {
            if (sensorId == Guid.Empty)
                return Json(new { hasCota = false });

            try
            {
                using var db = new SqlConnection(_sqlServerConn);
                var cotas = await db.QueryAsync<CotaSensor>(
                    @"SELECT * FROM CotaSensor 
                      WHERE IdSensor = @SensorId 
                      ORDER BY FechaRegistro DESC",
                    new { SensorId = sensorId });

                var cotaList = cotas.ToList();
                // La cota activa es la que no tiene Fin = true y tiene FechaFinal nula
                var active = cotaList.FirstOrDefault(c => c.Fin != true && c.FechaFinal == null);

                return Json(new
                {
                    hasCota = active != null,
                    active,
                    history = cotaList
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
