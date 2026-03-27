using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Dapper;
using CloudStationWeb.Models;
using System.Data;
using System.Text.Json;

namespace CloudStationWeb.Controllers
{
    [Authorize]
    public class RainForecastController : Controller
    {
        private readonly string _postgresConn;
        private readonly IConfiguration _configuration;
        private readonly Dictionary<string, string> _cuencaLabels;
        private readonly IHttpClientFactory _httpClientFactory;

        public RainForecastController(IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _postgresConn = configuration.GetConnectionString("PostgreSQL")
                ?? throw new InvalidOperationException("PostgreSQL connection string is required");

            // Build cuenca code → label map from config
            _cuencaLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ANG"] = "Angostura",
                ["MMT"] = "Chicoasén",
                ["MPS"] = "Malpaso",
                ["PEA"] = "Peñitas"
            };
            var kmlConfig = _configuration.GetSection("CuencasKml").Get<List<CuencaKmlConfig>>();
            if (kmlConfig != null)
            {
                foreach (var c in kmlConfig)
                    _cuencaLabels[c.Code.ToUpper()] = c.Label;
            }
        }

        // ── Main view ──
        public async Task<IActionResult> Index()
        {
            using IDbConnection pgDb = new NpgsqlConnection(_postgresConn);

            // Latest forecast
            var latest = await pgDb.QueryFirstOrDefaultAsync<RainForecast>(@"
                SELECT id AS Id, forecast_date::timestamp AS ForecastDate, model_run AS ModelRun,
                       file_name AS FileName, downloaded_at AS DownloadedAt, 
                       last_update AS LastUpdate, record_count AS RecordCount
                FROM rain_forecast.forecast 
                ORDER BY forecast_date DESC LIMIT 1");

            // Recent forecasts (last 10)
            var recent = (await pgDb.QueryAsync<RainForecast>(@"
                SELECT id AS Id, forecast_date::timestamp AS ForecastDate, model_run AS ModelRun,
                       file_name AS FileName, downloaded_at AS DownloadedAt,
                       last_update AS LastUpdate, record_count AS RecordCount
                FROM rain_forecast.forecast 
                ORDER BY forecast_date DESC LIMIT 10")).ToList();

            // Cuenca summaries for latest forecast
            var summaries = new List<ForecastCuencaSummary>();
            if (latest != null)
            {
                var cuencaCodes = await pgDb.QueryAsync<string>(@"
                    SELECT DISTINCT cuenca_code FROM rain_forecast.rain_record 
                    WHERE forecast_date = @D ORDER BY cuenca_code",
                    new { D = latest.ForecastDate });

                foreach (var code in cuencaCodes)
                {
                    var label = _cuencaLabels.TryGetValue(code, out var l) ? l : code;
                    var now = DateTime.UtcNow;

                    var acum = await pgDb.QueryFirstOrDefaultAsync<(double a24, double a48, double a72, double m24)>(@"
                        SELECT 
                            COALESCE(SUM(CASE WHEN ts <= @T24 THEN rain_mm ELSE 0 END) / NULLIF(COUNT(DISTINCT CASE WHEN ts <= @T24 THEN (latitude::text || longitude::text) END), 0), 0) AS a24,
                            COALESCE(SUM(CASE WHEN ts <= @T48 THEN rain_mm ELSE 0 END) / NULLIF(COUNT(DISTINCT CASE WHEN ts <= @T48 THEN (latitude::text || longitude::text) END), 0), 0) AS a48,
                            COALESCE(SUM(CASE WHEN ts <= @T72 THEN rain_mm ELSE 0 END) / NULLIF(COUNT(DISTINCT CASE WHEN ts <= @T72 THEN (latitude::text || longitude::text) END), 0), 0) AS a72,
                            COALESCE(MAX(CASE WHEN ts <= @T24 THEN rain_mm END), 0) AS m24
                        FROM rain_forecast.rain_record
                        WHERE forecast_date = @D AND cuenca_code = @C",
                        new { D = latest.ForecastDate, C = code, T24 = now.AddHours(24), T48 = now.AddHours(48), T72 = now.AddHours(72) });

                    // Hourly averages for chart (next 72h)
                    var hourly = (await pgDb.QueryAsync<ForecastHourBucket>(@"
                        SELECT time_bucket('1 hour', ts) AS Ts,
                               AVG(rain_mm) AS LluviaMediaMm
                        FROM rain_forecast.rain_record
                        WHERE forecast_date = @D AND cuenca_code = @C
                          AND ts >= @Now AND ts <= @End
                        GROUP BY time_bucket('1 hour', ts)
                        ORDER BY Ts",
                        new { D = latest.ForecastDate, C = code, Now = now, End = now.AddHours(72) })).ToList();

                    summaries.Add(new ForecastCuencaSummary
                    {
                        CuencaCode = code,
                        CuencaLabel = label,
                        LluviaAcum24h = acum.a24,
                        LluviaAcum48h = acum.a48,
                        LluviaAcum72h = acum.a72,
                        LluviaMax24h = acum.m24,
                        Hourly = hourly
                    });
                }
            }

            // Fetch external atmospheric conditions (non-blocking, best-effort)
            var condiciones = await ObtenerCondicionesAtmosfericasAsync();

            return View(new RainForecastIndexViewModel
            {
                LatestForecast = latest,
                CuencaSummaries = summaries,
                RecentForecasts = recent,
                ResumenMeteorologico = GenerarResumenMeteorologico(summaries, latest?.ForecastDate, condiciones)
            });
        }

        // ── Consulta de condiciones atmosféricas desde Open-Meteo (gratuito, sin API key) ──
        private async Task<CondicionesAtmosfericas?> ObtenerCondicionesAtmosfericasAsync()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var url = "https://api.open-meteo.com/v1/forecast?latitude=16.8&longitude=-93.2" +
                          "&current=temperature_2m,relative_humidity_2m,surface_pressure,wind_speed_10m,wind_direction_10m,weather_code" +
                          "&timezone=America/Mexico_City";
                var json = await client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var current = doc.RootElement.GetProperty("current");

                var weatherCode = current.GetProperty("weather_code").GetInt32();
                return new CondicionesAtmosfericas
                {
                    Temperatura = current.GetProperty("temperature_2m").GetDouble(),
                    HumedadRelativa = current.GetProperty("relative_humidity_2m").GetInt32(),
                    PresionAtm = current.GetProperty("surface_pressure").GetDouble(),
                    VientoVelocidad = current.GetProperty("wind_speed_10m").GetDouble(),
                    VientoDireccion = current.GetProperty("wind_direction_10m").GetDouble(),
                    CodigoTiempo = weatherCode,
                    DescripcionTiempo = DescribirCodigoTiempo(weatherCode)
                };
            }
            catch
            {
                return null;
            }
        }

        private static string DescribirCodigoTiempo(int code) => code switch
        {
            0 => "cielo despejado",
            1 => "mayormente despejado",
            2 => "parcialmente nublado",
            3 => "nublado",
            45 or 48 => "niebla",
            51 or 53 or 55 => "llovizna",
            56 or 57 => "llovizna helada",
            61 => "lluvia ligera",
            63 => "lluvia moderada",
            65 => "lluvia fuerte",
            66 or 67 => "lluvia helada",
            71 or 73 or 75 => "nevada",
            77 => "granizo ligero",
            80 => "chubascos ligeros",
            81 => "chubascos moderados",
            82 => "chubascos fuertes",
            85 or 86 => "chubascos de nieve",
            95 => "tormenta eléctrica",
            96 or 99 => "tormenta eléctrica con granizo",
            _ => "sin datos"
        };

        private static string DescribirDireccionViento(double grados)
        {
            var dirs = new[] { "Norte", "Noreste", "Este", "Sureste", "Sur", "Suroeste", "Oeste", "Noroeste" };
            var idx = (int)Math.Round(grados / 45.0) % 8;
            return dirs[idx];
        }

        // ── Generación de boletín meteorológico inteligente ──
        private string GenerarResumenMeteorologico(List<ForecastCuencaSummary> cuencas, DateTime? forecastDate, CondicionesAtmosfericas? condiciones = null)
        {
            if (cuencas == null || !cuencas.Any() || forecastDate == null)
                return "";

            var sb = new System.Text.StringBuilder();
            var fecha = forecastDate.Value;

            // ── Párrafo 0: Condiciones atmosféricas actuales (API externa) ──
            if (condiciones != null)
            {
                sb.Append("<strong><i class=\"thermometer half icon\"></i> Condiciones atmosféricas actuales</strong> ");
                sb.Append("(fuente: Open-Meteo · cuenca del Grijalva):<br>");
                sb.Append("Cielo con <strong>");
                sb.Append(condiciones.DescripcionTiempo);
                sb.Append("</strong>, temperatura de <strong>");
                sb.Append(condiciones.Temperatura.ToString("F1"));
                sb.Append(" °C</strong>, humedad relativa del <strong>");
                sb.Append(condiciones.HumedadRelativa);
                sb.Append("%</strong>");

                // Interpretar humedad
                if (condiciones.HumedadRelativa >= 80)
                    sb.Append(" (ambiente muy húmedo, favorable para desarrollo de lluvias)");
                else if (condiciones.HumedadRelativa >= 60)
                    sb.Append(" (humedad moderada)");
                else
                    sb.Append(" (ambiente relativamente seco)");

                sb.Append(". Presión atmosférica de <strong>");
                sb.Append(condiciones.PresionAtm.ToString("F0"));
                sb.Append(" hPa</strong>");

                // Interpretar presión
                if (condiciones.PresionAtm < 1005)
                    sb.Append(" <span style=\"color:#ff9800;\">(baja, posible inestabilidad atmosférica)</span>");
                else if (condiciones.PresionAtm > 1020)
                    sb.Append(" (alta, condiciones estables)");

                sb.Append(". Viento del <strong>");
                sb.Append(DescribirDireccionViento(condiciones.VientoDireccion));
                sb.Append("</strong> a <strong>");
                sb.Append(condiciones.VientoVelocidad.ToString("F0"));
                sb.Append(" km/h</strong>");

                if (condiciones.VientoVelocidad >= 40)
                    sb.Append(" <span style=\"color:#f44336;\">(vientos fuertes)</span>");
                else if (condiciones.VientoVelocidad >= 20)
                    sb.Append(" (moderado)");
                else
                    sb.Append(" (ligero)");

                sb.Append(".<br><br>");
            }

            // ── Párrafo 1: Pronóstico próximas 24h ──
            sb.Append("<strong>Pronóstico de lluvia para las próximas 24 h</strong> (modelo numérico ");
            sb.Append(fecha.ToString("dd/MMM/yyyy"));
            sb.Append(" corrida 00Z):<br><br>");

            // Clasificar cuencas por intensidad
            var conLluvia = cuencas.Where(c => c.LluviaAcum24h >= 1).OrderByDescending(c => c.LluviaAcum24h).ToList();
            var sinLluvia = cuencas.Where(c => c.LluviaAcum24h < 1).ToList();

            if (!conLluvia.Any())
            {
                sb.Append("No se prevén lluvias significativas sobre la cuenca del río Grijalva en las próximas 24 horas.");
            }
            else
            {
                // Lluvia total promedio sobre toda la cuenca
                var promGeneral = cuencas.Average(c => c.LluviaAcum24h);
                var maxPuntual = cuencas.Max(c => c.LluviaMax24h);
                var cuencaMaxPuntual = cuencas.First(c => c.LluviaMax24h == maxPuntual);

                sb.Append("El modelo numérico de pronóstico prevé ");
                sb.Append(DescribirIntensidad(promGeneral));
                sb.Append(" sobre la cuenca del río Grijalva");

                // Detallar por cuenca
                sb.Append(". ");
                foreach (var c in conLluvia)
                {
                    var rango = CalcularRango(c.LluviaAcum24h, c.LluviaMax24h);
                    sb.Append("Para la cuenca de <strong>");
                    sb.Append(c.CuencaLabel);
                    sb.Append("</strong> se esperan ");
                    sb.Append(DescribirIntensidad(c.LluviaAcum24h));
                    sb.Append(" con acumulados promedio de <strong>");
                    sb.Append(c.LluviaAcum24h.ToString("F0"));
                    sb.Append(" mm</strong>");
                    if (c.LluviaMax24h > c.LluviaAcum24h * 1.5)
                    {
                        sb.Append(" y puntuales de hasta <strong>");
                        sb.Append(c.LluviaMax24h.ToString("F0"));
                        sb.Append(" mm</strong>");
                    }
                    sb.Append(". ");
                }

                if (sinLluvia.Any())
                {
                    sb.Append("Para ");
                    sb.Append(string.Join(" y ", sinLluvia.Select(c => c.CuencaLabel)));
                    sb.Append(" no se prevén lluvias significativas. ");
                }

                // Máximo puntual destacado
                if (maxPuntual >= 25)
                {
                    sb.Append("<br><br><i class=\"exclamation triangle icon\" style=\"color:#fdd835;\"></i> ");
                    sb.Append("<strong style=\"color:#fdd835;\">Atención:</strong> se pronostican lluvias puntuales de hasta <strong>");
                    sb.Append(maxPuntual.ToString("F0"));
                    sb.Append(" mm</strong> en la subcuenca de ");
                    sb.Append(cuencaMaxPuntual.CuencaLabel);
                    sb.Append(", lo que podría generar incrementos en los niveles de los ríos.");
                }
            }

            // ── Párrafo 2: Tendencia 48-72h ──
            var prom48 = cuencas.Average(c => c.LluviaAcum48h);
            var prom72 = cuencas.Average(c => c.LluviaAcum72h);
            var tendencia48vs24 = prom48 - cuencas.Average(c => c.LluviaAcum24h);

            sb.Append("<br><br><strong>Tendencia 48-72 h:</strong> ");
            if (tendencia48vs24 > cuencas.Average(c => c.LluviaAcum24h) * 0.5)
                sb.Append("Se prevé un <strong style=\"color:#ff9800;\">incremento</strong> en la actividad pluvial ");
            else if (tendencia48vs24 < -cuencas.Average(c => c.LluviaAcum24h) * 0.3)
                sb.Append("Se prevé una <strong style=\"color:#81c784;\">disminución</strong> en la actividad pluvial ");
            else
                sb.Append("Se prevé que las condiciones de lluvia se <strong>mantengan similares</strong> ");

            sb.Append("durante las próximas 48 a 72 horas, con acumulados promedio de ");
            sb.Append(prom48.ToString("F0"));
            sb.Append("–");
            sb.Append(prom72.ToString("F0"));
            sb.Append(" mm sobre la cuenca del río Grijalva.");

            return sb.ToString();
        }

        private static string DescribirIntensidad(double mm)
        {
            if (mm >= 75) return "lluvias <strong style=\"color:#f44336;\">muy fuertes</strong>";
            if (mm >= 50) return "lluvias <strong style=\"color:#ff5722;\">fuertes</strong>";
            if (mm >= 25) return "chubascos <strong style=\"color:#ff9800;\">moderados a fuertes</strong>";
            if (mm >= 10) return "lluvias aisladas y chubascos de <strong>ligeros a moderados</strong>";
            if (mm >= 5) return "lluvias <strong>ligeras</strong> aisladas";
            if (mm >= 1) return "lluvias <strong>escasas</strong>";
            return "condiciones secas";
        }

        private static string CalcularRango(double promedio, double max)
        {
            var min = Math.Max(1, promedio * 0.5);
            return $"{min:F0} a {max:F0} mm";
        }

        // ── API: Heatmap grid points for a specific time range ──
        [HttpGet]
        public async Task<JsonResult> GetHeatmapData(string? forecastDate = null, int hoursAhead = 24)
        {
            using IDbConnection pgDb = new NpgsqlConnection(_postgresConn);

            DateTime fd;
            if (!string.IsNullOrEmpty(forecastDate) && DateTime.TryParse(forecastDate, out var parsed))
                fd = parsed.Date;
            else
            {
                var latestDate = await pgDb.QueryFirstOrDefaultAsync<DateTime?>(
                    "SELECT MAX(forecast_date::timestamp) FROM rain_forecast.forecast");
                fd = latestDate?.Date ?? DateTime.UtcNow.Date;
            }

            var now = DateTime.UtcNow;
            var data = await pgDb.QueryAsync<ForecastGridPoint>(@"
                SELECT latitude AS Lat, longitude AS Lon, 
                       SUM(rain_mm) AS Rain
                FROM rain_forecast.rain_record
                WHERE forecast_date = @D 
                  AND ts >= @Now AND ts <= @End
                GROUP BY latitude, longitude
                HAVING SUM(rain_mm) > 0",
                new { D = fd, Now = now, End = now.AddHours(hoursAhead) });

            return Json(new { forecastDate = fd.ToString("yyyy-MM-dd"), points = data });
        }

        // ── API: Hourly forecast for a specific cuenca ──
        [HttpGet]
        public async Task<JsonResult> GetCuencaHourly(string cuencaCode, string? forecastDate = null)
        {
            using IDbConnection pgDb = new NpgsqlConnection(_postgresConn);

            DateTime fd;
            if (!string.IsNullOrEmpty(forecastDate) && DateTime.TryParse(forecastDate, out var parsed))
                fd = parsed.Date;
            else
            {
                var latestDate = await pgDb.QueryFirstOrDefaultAsync<DateTime?>(
                    "SELECT MAX(forecast_date::timestamp) FROM rain_forecast.forecast");
                fd = latestDate?.Date ?? DateTime.UtcNow.Date;
            }

            var data = await pgDb.QueryAsync<ForecastHourlySummary>(@"
                SELECT time_bucket('1 hour', ts) AS Ts,
                       forecast_date::timestamp AS ForecastDate,
                       cuenca_code AS CuencaCode,
                       'Cuenca' AS SubcuencaName,
                       AVG(rain_mm) AS LluviaMediaMm,
                       MAX(rain_mm) AS LluviaMaxMm,
                       COUNT(*) AS NumPuntos
                FROM rain_forecast.rain_record
                WHERE forecast_date = @D AND cuenca_code = @C
                GROUP BY time_bucket('1 hour', ts), forecast_date, cuenca_code
                ORDER BY Ts",
                new { D = fd, C = cuencaCode });

            var label = _cuencaLabels.TryGetValue(cuencaCode, out var l) ? l : cuencaCode;
            return Json(new { cuencaCode, cuencaLabel = label, forecastDate = fd.ToString("yyyy-MM-dd"), data });
        }

        // ── API: Subcuenca breakdown ──
        [HttpGet]
        public async Task<JsonResult> GetSubcuencaDetail(string cuencaCode, string? forecastDate = null)
        {
            using IDbConnection pgDb = new NpgsqlConnection(_postgresConn);

            DateTime fd;
            if (!string.IsNullOrEmpty(forecastDate) && DateTime.TryParse(forecastDate, out var parsed))
                fd = parsed.Date;
            else
            {
                var latestDate = await pgDb.QueryFirstOrDefaultAsync<DateTime?>(
                    "SELECT MAX(forecast_date::timestamp) FROM rain_forecast.forecast");
                fd = latestDate?.Date ?? DateTime.UtcNow.Date;
            }

            var now = DateTime.UtcNow;

            // Acumulados por subcuenca
            var subs = (await pgDb.QueryAsync<ForecastSubcuencaDetail>(@"
                SELECT cuenca_code AS CuencaCode,
                       subcuenca_name AS SubcuencaName,
                       COALESCE(SUM(CASE WHEN ts <= @T24 THEN rain_mm ELSE 0 END) / 
                           NULLIF(COUNT(DISTINCT CASE WHEN ts <= @T24 THEN (latitude::text || longitude::text) END), 0), 0) AS LluviaAcum24h,
                       COALESCE(SUM(CASE WHEN ts <= @T48 THEN rain_mm ELSE 0 END) / 
                           NULLIF(COUNT(DISTINCT CASE WHEN ts <= @T48 THEN (latitude::text || longitude::text) END), 0), 0) AS LluviaAcum48h
                FROM rain_forecast.rain_record
                WHERE forecast_date = @D AND cuenca_code = @C
                  AND ts >= @Now
                GROUP BY cuenca_code, subcuenca_name
                ORDER BY subcuenca_name",
                new { D = fd, C = cuencaCode, Now = now, T24 = now.AddHours(24), T48 = now.AddHours(48) })).ToList();

            return Json(new { cuencaCode, forecastDate = fd.ToString("yyyy-MM-dd"), subcuencas = subs });
        }

        // ── API: Compare forecast vs observed ──
        [HttpGet]
        public async Task<JsonResult> GetForecastVsObserved(string cuencaCode, string? forecastDate = null)
        {
            using IDbConnection pgDb = new NpgsqlConnection(_postgresConn);

            DateTime fd;
            if (!string.IsNullOrEmpty(forecastDate) && DateTime.TryParse(forecastDate, out var parsed))
                fd = parsed.Date;
            else
                fd = DateTime.UtcNow.Date;

            // Forecast hourly (past 24h from forecast)
            var forecast = (await pgDb.QueryAsync(@"
                SELECT time_bucket('1 hour', ts) AS ts,
                       AVG(rain_mm) AS lluvia_mm
                FROM rain_forecast.rain_record
                WHERE forecast_date = @D AND cuenca_code = @C
                  AND ts >= @Start AND ts <= @End
                GROUP BY time_bucket('1 hour', ts)
                ORDER BY ts",
                new { D = fd, C = cuencaCode, Start = fd.ToUniversalTime(), End = DateTime.UtcNow })).ToList();

            // Observed hourly (from main resumen_horario)
            // First get stations in this cuenca
            var sqlServerConn = _configuration.GetConnectionString("SqlServer")!;
            List<string> stationIds;
            using (IDbConnection sqlDb = new Microsoft.Data.SqlClient.SqlConnection(sqlServerConn))
            {
                stationIds = (await sqlDb.QueryAsync<string>(@"
                    SELECT e.IdAsignado 
                    FROM Estacion e
                    JOIN Cuenca c ON e.IdCuenca = c.Id
                    WHERE c.Codigo = @Code AND e.Activo = 1",
                    new { Code = cuencaCode })).ToList();
            }

            var observed = new List<dynamic>();
            if (stationIds.Any())
            {
                observed = (await pgDb.QueryAsync(@"
                    SELECT time_bucket('1 hour', ts) AS ts,
                           AVG(acumulado) AS lluvia_mm
                    FROM public.resumen_horario
                    WHERE variable = 'precipitación'
                      AND id_asignado = ANY(@Ids)
                      AND ts >= @Start AND ts <= @End
                    GROUP BY time_bucket('1 hour', ts)
                    ORDER BY ts",
                    new { Ids = stationIds.ToArray(), Start = fd.ToUniversalTime(), End = DateTime.UtcNow })).ToList();
            }

            return Json(new { forecastDate = fd.ToString("yyyy-MM-dd"), cuencaCode, forecast, observed });
        }

        // ── API: Available forecast dates ──
        [HttpGet]
        public async Task<JsonResult> GetAvailableDates()
        {
            using IDbConnection pgDb = new NpgsqlConnection(_postgresConn);
            var dates = await pgDb.QueryAsync<DateTime>(@"
                SELECT forecast_date::timestamp FROM rain_forecast.forecast 
                ORDER BY forecast_date DESC LIMIT 30");
            return Json(dates.Select(d => d.ToString("yyyy-MM-dd")));
        }
    }
}
