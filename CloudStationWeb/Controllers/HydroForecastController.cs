using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CloudStationWeb.Services;

namespace CloudStationWeb.Controllers
{
    [Authorize]
    public class HydroForecastController : Controller
    {
        private readonly HydroForecastService _hydroService;
        private readonly ILogger<HydroForecastController> _logger;

        public HydroForecastController(HydroForecastService hydroService, ILogger<HydroForecastController> logger)
        {
            _hydroService = hydroService;
            _logger = logger;
        }

        /// <summary>
        /// API: /HydroForecast/GetInputData?horizonHours=72
        /// Devuelve los datos de entrada que alimentan la simulación.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetInputData(int horizonHours = 72)
        {
            try
            {
                if (horizonHours < 1) horizonHours = 24;
                if (horizonHours > 360) horizonHours = 360;
                var data = await _hydroService.GetInputDataAsync(horizonHours);
                return Json(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching hydro input data");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// API: /HydroForecast/RunSimulation
        /// Ejecuta la simulación hidrológica con extracciones opcionales editadas.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RunSimulation([FromBody] SimulationRequest request)
        {
            try
            {
                int horizonHours = request?.HorizonHours ?? 72;
                if (horizonHours < 1) horizonHours = 24;
                if (horizonHours > 360) horizonHours = 360; // máximo 15 días

                var extractions = request?.Extractions ?? new Dictionary<string, double>();
                var extractionSchedule = request?.ExtractionSchedule;
                var aportationSchedule = request?.AportationSchedule;
                var drainCoefficients = request?.DrainCoefficients;
                var curveNumbers = request?.CurveNumbers;
                var result = await _hydroService.RunSimulationAsync(
                    horizonHours, extractions, extractionSchedule,
                    aportationSchedule, drainCoefficients, curveNumbers);

                // Formatear para el frontend
                var response = new
                {
                    success = true,
                    generatedAt = result.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    forecastDate = result.ForecastDate?.ToString("yyyy-MM-dd"),
                    horizonHours = result.HorizonHours,
                    dams = result.DamSimulations.Select(d => new
                    {
                        damName = d.DamName,
                        cuencaCode = d.CuencaCode,
                        initialElev = Math.Round(d.InitialElevation, 2),
                        finalElev = Math.Round(d.FinalElevation, 2),
                        maxElev = Math.Round(d.MaxElevation, 2),
                        minElev = Math.Round(d.MinElevation, 2),
                        initialStorage = Math.Round(d.InitialStorageMm3, 2),
                        finalStorage = Math.Round(d.FinalStorageMm3, 2),
                        deltaElev = Math.Round(d.FinalElevation - d.InitialElevation, 2),
                        deltaStorage = Math.Round(d.FinalStorageMm3 - d.InitialStorageMm3, 2),
                        hourly = d.HourlyData.Select(h => new
                        {
                            time = h.Time.ToString("yyyy-MM-dd HH:mm"),
                            hour = h.Hour,
                            elev = Math.Round(h.Elevation, 2),
                            storage = Math.Round(h.StorageMm3, 2),
                            basinInput = Math.Round(h.BasinInputMm3, 4),
                            upstreamInput = Math.Round(h.UpstreamInputMm3, 4),
                            extraction = Math.Round(h.ExtractionMm3, 4),
                            rain = Math.Round(h.RainMm, 2)
                        })
                    })
                };

                return Json(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running hydro simulation");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }

    public class SimulationRequest
    {
        public int HorizonHours { get; set; } = 72;
        /// <summary>Extracción constante por presa (m³/s). Compatibilidad hacia atrás.</summary>
        public Dictionary<string, double>? Extractions { get; set; }
        /// <summary>Extracción variable por presa y día: { "Angostura": [120.5, 130.0, ...], ... } (m³/s por día)</summary>
        public Dictionary<string, List<double>>? ExtractionSchedule { get; set; }
        /// <summary>Aportación variable por presa y día (m³/s por día). Reemplaza la aportación calculada por lluvia.</summary>
        public Dictionary<string, List<double>>? AportationSchedule { get; set; }
        /// <summary>Coeficientes de escurrimiento editados por el usuario (por presa).</summary>
        public Dictionary<string, double>? DrainCoefficients { get; set; }
        /// <summary>Curva Number editados por el usuario (por presa).</summary>
        public Dictionary<string, double>? CurveNumbers { get; set; }
    }
}
