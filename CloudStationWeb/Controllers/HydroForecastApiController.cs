using Microsoft.AspNetCore.Mvc;
using CloudStationWeb.Services;

namespace CloudStationWeb.Controllers
{
    /// <summary>
    /// API pública de pronóstico hidrológico para consumo externo.
    /// Formato de salida compatible con grijalva-hydro-model-service (Spring Boot).
    /// Autenticación: Header X-Api-Key ó JWT Bearer con rol ApiConsumer/SuperAdmin/Administrador.
    ///
    /// GET  /api/hydro/input?horizonHours=72              → Datos de entrada del modelo
    /// POST /api/hydro/simulate                           → Ejecutar simulación (formato Spring Boot)
    /// GET  /api/hydro/trend?realDays=5&forecastHours=72  → Datos reales + pronóstico combinados
    /// GET  /api/hydro/dams                               → Catálogo de presas y parámetros
    /// </summary>
    [Route("api/hydro")]
    [ApiController]
    public class HydroForecastApiController : ControllerBase
    {
        private readonly HydroForecastService _hydroService;
        private readonly FunVasosService _funVasosService;
        private readonly string _apiKey;
        private readonly ILogger<HydroForecastApiController> _logger;

        // Metadatos de cada presa compatible con Dam.java del microservicio Spring Boot
        private static readonly Dictionary<string, DamMeta> DamMetadata = new()
        {
            ["Angostura"] = new(1, 1, "ANG", "Angostura", 542.10f, 539.00f, 510, false, "HUI"),
            ["Chicoasen"] = new(2, 2, "CHI", "Chicoasén", 400.00f, 395.00f, 378, true, "HUI"),
            ["Malpaso"] = new(3, 3, "MAL", "Malpaso", 192.00f, 189.70f, 163, true, "HUI"),
            ["JGrijalva"] = new(4, 4, "JGR", "Tapón Juan Grijalva", 105.50f, 100.00f, 87, true, "HUI"),
            ["Penitas"] = new(5, 5, "PEN", "Peñitas", 99.20f, 95.10f, 84, true, "HUI")
        };

        public HydroForecastApiController(
            HydroForecastService hydroService,
            FunVasosService funVasosService,
            IConfiguration config,
            ILogger<HydroForecastApiController> logger)
        {
            _hydroService = hydroService;
            _funVasosService = funVasosService;
            _apiKey = config["ImageStore:ApiKey"] ?? "pih-default-key-change-me";
            _logger = logger;
        }

        /// <summary>
        /// Datos de entrada del modelo hidrológico.
        /// GET /api/hydro/input?horizonHours=72
        /// </summary>
        [HttpGet("input")]
        public async Task<IActionResult> GetInputData([FromQuery] int horizonHours = 72)
        {
            if (!ValidateAuth())
                return Unauthorized(new { error = "API key inválida o usuario no autorizado" });

            horizonHours = Math.Clamp(horizonHours, 1, 360);
            var data = await _hydroService.GetInputDataAsync(horizonHours);
            return Ok(data);
        }

        /// <summary>
        /// Ejecuta simulación hidrológica completa.
        /// Formato de respuesta compatible con HydroModel de Spring Boot.
        /// POST /api/hydro/simulate
        /// Body: { horizonHours, extractions, extractionSchedule, curveNumbers, drainCoefficients }
        /// Retorna: Array de HydroModel (uno por presa, en orden de cascada).
        /// </summary>
        [HttpPost("simulate")]
        public async Task<IActionResult> RunSimulation([FromBody] SimulationApiRequest request)
        {
            if (!ValidateAuth())
                return Unauthorized(new { error = "API key inválida o usuario no autorizado" });

            int horizonHours = Math.Clamp(request?.HorizonHours ?? 72, 1, 360);

            var result = await _hydroService.RunSimulationAsync(
                horizonHours,
                request?.Extractions,
                request?.ExtractionSchedule,
                request?.AportationSchedule,
                request?.DrainCoefficients,
                request?.CurveNumbers);

            // Formato compatible con HydroModel.java del microservicio Spring Boot
            var hydroModels = result.DamSimulations.Select(d =>
            {
                var meta = DamMetadata.GetValueOrDefault(d.DamName);
                var forecastDate = result.ForecastDate ?? DateTime.UtcNow.Date;

                return new
                {
                    subBasinId = meta?.SubBasinId ?? 0,
                    modelType = meta?.ModelType ?? "HUI",
                    date = forecastDate.ToString("yyyy-MM-dd"),
                    dam = new
                    {
                        id = meta?.Id ?? 0,
                        centralId = meta?.CentralId ?? 0,
                        code = meta?.Code ?? d.DamName,
                        description = meta?.Description ?? d.DamName,
                        nameValue = meta?.NameValue,
                        namoValue = meta?.NamoValue,
                        naminoValue = meta?.NaminoValue,
                        hasPreviousDam = meta?.HasPreviousDam ?? false
                    },
                    records = d.HourlyData.Select(h => new
                    {
                        date = h.Time.ToString("yyyy-MM-dd"),
                        rain = Math.Round(h.RainMm, 2),
                        extractionPreviousDam = Math.Round(h.UpstreamInputMm3, 4),
                        elevation = Math.Round(h.Elevation, 2),
                        extraction = Math.Round(h.ExtractionMm3, 4),
                        basinInput = Math.Round(h.BasinInputMm3, 4),
                        totalCapacity = Math.Round(h.StorageMm3, 2),
                        forecast = true,
                        hour = h.Hour
                    })
                };
            });

            return Ok(hydroModels);
        }

        /// <summary>
        /// Datos combinados: N días reales + N horas de pronóstico.
        /// GET /api/hydro/trend?realDays=5&forecastHours=72
        /// Ideal para gráficas de tendencia.
        /// </summary>
        [HttpGet("trend")]
        public async Task<IActionResult> GetTrendData([FromQuery] int realDays = 5, [FromQuery] int forecastHours = 72)
        {
            if (!ValidateAuth())
                return Unauthorized(new { error = "API key inválida o usuario no autorizado" });

            realDays = Math.Clamp(realDays, 1, 30);
            forecastHours = Math.Clamp(forecastHours, 1, 360);

            // 1. Datos reales de FunVasos (últimos N días)
            var endDate = DateTime.UtcNow.Date;
            var startDate = endDate.AddDays(-realDays);
            var realData = await _funVasosService.GetDataAsync(startDate, endDate);

            // 2. Pronóstico
            var forecast = await _hydroService.RunSimulationAsync(forecastHours);

            // 3. Combinar por presa
            var presaToHydro = new Dictionary<string, string>
            {
                ["Angostura"] = "Angostura",
                ["Chicoasén"] = "Chicoasen",
                ["Malpaso"] = "Malpaso",
                ["Tapón Juan Grijalva"] = "JGrijalva",
                ["Peñitas"] = "Penitas"
            };

            var hydroModels = new List<object>();

            foreach (var sim in forecast.DamSimulations)
            {
                var meta = DamMetadata.GetValueOrDefault(sim.DamName);
                var forecastDate = forecast.ForecastDate ?? DateTime.UtcNow.Date;

                // Buscar datos reales correspondientes
                var realPresa = realData.Presas.FirstOrDefault(p =>
                    presaToHydro.GetValueOrDefault(p.Presa) == sim.DamName);

                // Series reales → formato HydroModelRecord
                var realRecords = new List<object>();
                if (realPresa != null)
                {
                    foreach (var d in realPresa.Datos)
                    {
                        realRecords.Add(new
                        {
                            date = d.Ts.ToString("yyyy-MM-dd"),
                            rain = 0.0,
                            extractionPreviousDam = 0.0,
                            elevation = d.Elevacion.HasValue ? Math.Round(d.Elevacion.Value, 2) : 0.0,
                            extraction = d.ExtraccionesTotalQ.HasValue ? Math.Round(d.ExtraccionesTotalQ.Value, 2) : 0.0,
                            basinInput = d.AportacionesQ.HasValue ? Math.Round(d.AportacionesQ.Value, 2) : 0.0,
                            totalCapacity = d.Almacenamiento.HasValue ? Math.Round(d.Almacenamiento.Value, 2) : 0.0,
                            forecast = false,
                            hour = d.Hora
                        });
                    }
                }

                // Series pronóstico → formato HydroModelRecord
                var forecastRecords = sim.HourlyData.Select(h => new
                {
                    date = h.Time.ToString("yyyy-MM-dd"),
                    rain = Math.Round(h.RainMm, 2),
                    extractionPreviousDam = Math.Round(h.UpstreamInputMm3, 4),
                    elevation = Math.Round(h.Elevation, 2),
                    extraction = Math.Round(h.ExtractionMm3, 4),
                    basinInput = Math.Round(h.BasinInputMm3, 4),
                    totalCapacity = Math.Round(h.StorageMm3, 2),
                    forecast = true,
                    hour = h.Hour
                });

                hydroModels.Add(new
                {
                    subBasinId = meta?.SubBasinId ?? 0,
                    modelType = meta?.ModelType ?? "HUI",
                    date = forecastDate.ToString("yyyy-MM-dd"),
                    dam = new
                    {
                        id = meta?.Id ?? 0,
                        centralId = meta?.CentralId ?? 0,
                        code = meta?.Code ?? sim.DamName,
                        description = meta?.Description ?? sim.DamName,
                        nameValue = meta?.NameValue,
                        namoValue = meta?.NamoValue,
                        naminoValue = meta?.NaminoValue,
                        hasPreviousDam = meta?.HasPreviousDam ?? false
                    },
                    records = realRecords.Concat<object>(forecastRecords).ToList()
                });
            }

            return Ok(hydroModels);
        }

        /// <summary>
        /// Catálogo de presas con niveles de referencia.
        /// Formato compatible con Dam.java del microservicio Spring Boot.
        /// GET /api/hydro/dams
        /// </summary>
        [HttpGet("dams")]
        public IActionResult GetDams()
        {
            if (!ValidateAuth())
                return Unauthorized(new { error = "API key inválida o usuario no autorizado" });

            var dams = DamMetadata.Select(kv => new
            {
                id = kv.Value.Id,
                centralId = kv.Value.CentralId,
                code = kv.Value.Code,
                description = kv.Value.Description,
                nameValue = kv.Value.NameValue,
                namoValue = kv.Value.NamoValue,
                naminoValue = kv.Value.NaminoValue,
                hasPreviousDam = kv.Value.HasPreviousDam,
                modelType = kv.Value.ModelType
            });

            return Ok(dams);
        }

        /// <summary>
        /// Valida autenticación: acepta X-Api-Key header ó JWT Bearer token
        /// con rol ApiConsumer, SuperAdmin o Administrador.
        /// </summary>
        private bool ValidateAuth()
        {
            // Opción 1: API Key en header
            if (Request.Headers.TryGetValue("X-Api-Key", out var key) &&
                string.Equals(key, _apiKey, StringComparison.Ordinal))
                return true;

            // Opción 2: JWT Bearer con rol autorizado
            if (User.Identity?.IsAuthenticated == true &&
                (User.IsInRole("ApiConsumer") || User.IsInRole("SuperAdmin") || User.IsInRole("Administrador")))
                return true;

            return false;
        }

        private record DamMeta(
            int Id, int CentralId, string Code, string Description,
            float NameValue, float NamoValue, int NaminoValue,
            bool HasPreviousDam, string ModelType)
        {
            public int SubBasinId => Id;
        }
    }

    public class SimulationApiRequest
    {
        public int HorizonHours { get; set; } = 72;
        public Dictionary<string, double>? Extractions { get; set; }
        public Dictionary<string, List<double>>? ExtractionSchedule { get; set; }
        public Dictionary<string, List<double>>? AportationSchedule { get; set; }
        public Dictionary<string, double>? DrainCoefficients { get; set; }
        public Dictionary<string, double>? CurveNumbers { get; set; }
    }
}
