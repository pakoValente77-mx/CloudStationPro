using System.Data;
using CloudStationWeb.Services;
using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace CloudStationWeb.Controllers
{
    /// <summary>
    /// API REST de reportes de precipitación — consumible desde iOS, Android, Windows, etc.
    /// Requiere autenticación JWT (Bearer token) o cookie de sesión web.
    /// </summary>
    [Route("api/lluvia")]
    [ApiController]
    [Authorize(AuthenticationSchemes = $"Identity.Application,{JwtBearerDefaults.AuthenticationScheme}")]
    public class RainfallApiController : ControllerBase
    {
        private readonly string _pgConn;
        private readonly string _sqlConn;

        // UTC-6 (hora local CFE)
        private static readonly TimeSpan UtcMinus6 = TimeSpan.FromHours(-6);

        public RainfallApiController(IConfiguration config)
        {
            _pgConn = config.GetConnectionString("PostgreSQL") ?? "";
            _sqlConn = config.GetConnectionString("SqlServer") ?? "";
        }

        // ── Modelos de respuesta ────────────────────────────────────

        public class EstacionLluvia
        {
            public string IdAsignado { get; set; } = "";
            public string DcpId { get; set; } = "";
            public string Nombre { get; set; } = "";
            public string Cuenca { get; set; } = "";
            public string Subcuenca { get; set; } = "";
            public double AcumuladoMm { get; set; }
            public int HorasConDato { get; set; }
        }

        public class CuencaLluvia
        {
            public string Nombre { get; set; } = "";
            public string Tipo { get; set; } = ""; // cuenca | subcuenca
            public double PromedioMm { get; set; }
            public double MaxMm { get; set; }
            public double MinMm { get; set; }
            public int EstacionesConDato { get; set; }
            public int EstacionesTotal { get; set; }
            public string Semaforo { get; set; } = "verde";
        }

        public class SubcuencaReporte
        {
            public string Subcuenca { get; set; } = "";
            public List<EstacionLluvia> Estaciones { get; set; } = new();
            public double PromedioMm { get; set; }
        }

        public class HoraLluvia
        {
            public string HoraUtc { get; set; } = "";
            public string HoraLocal { get; set; } = "";
            public double AcumuladoMm { get; set; }
        }

        public class EstacionHorario
        {
            public string IdAsignado { get; set; } = "";
            public string Nombre { get; set; } = "";
            public string Cuenca { get; set; } = "";
            public List<HoraLluvia> Horas { get; set; } = new();
            public double TotalMm { get; set; }
        }

        // ── Endpoints ───────────────────────────────────────────────

        /// <summary>
        /// Lluvia acumulada 24h: de 6:00 AM (ayer) a 6:00 AM (hoy), hora local UTC-6.
        /// </summary>
        [HttpGet("24h")]
        public async Task<IActionResult> Reporte24h([FromQuery] string? fecha = null)
        {
            var (ayer6am, hoy6am, ahora) = CalcularPeriodos(fecha);
            var estaciones = await ConsultarAcumulado(ayer6am, hoy6am);
            var cuencas = AgruparPorCuenca(estaciones);

            return Ok(new
            {
                tipo = "24h",
                periodoInicio = ayer6am.ToString("o"),
                periodoFin = hoy6am.ToString("o"),
                generado = ahora.ToString("o"),
                totalEstaciones = estaciones.Count,
                estaciones,
                cuencas,
            });
        }

        /// <summary>
        /// Lluvia acumulada parcial: de 6:00 AM (hoy) a la hora de consulta.
        /// </summary>
        [HttpGet("parcial")]
        public async Task<IActionResult> ReporteParcial([FromQuery] string? fecha = null)
        {
            var (_, hoy6am, ahora) = CalcularPeriodos(fecha);
            var estaciones = await ConsultarAcumulado(hoy6am, ahora);
            var cuencas = AgruparPorCuenca(estaciones);

            return Ok(new
            {
                tipo = "parcial",
                periodoInicio = hoy6am.ToString("o"),
                periodoFin = ahora.ToString("o"),
                generado = ahora.ToString("o"),
                totalEstaciones = estaciones.Count,
                estaciones,
                cuencas,
            });
        }

        /// <summary>
        /// Desglose hora por hora de precipitación (para gráficas).
        /// </summary>
        [HttpGet("horario")]
        public async Task<IActionResult> ReporteHorario(
            [FromQuery] string tipo = "24h",
            [FromQuery] string? fecha = null)
        {
            var (ayer6am, hoy6am, ahora) = CalcularPeriodos(fecha);
            DateTimeOffset inicio, fin;
            if (tipo == "24h")
            { inicio = ayer6am; fin = hoy6am; }
            else
            { inicio = hoy6am; fin = ahora; }

            // Obtener metadata de estaciones CFE
            var meta = await CargarMetaCfe();

            // Consultar desglose horario
            using var pg = new NpgsqlConnection(_pgConn);
            var rows = await pg.QueryAsync<(string id_asignado, DateTimeOffset ts, double acumulado)>(
                @"SELECT id_asignado, ts, COALESCE(acumulado, 0)
                  FROM resumen_horario
                  WHERE variable = 'precipitación'
                    AND id_asignado LIKE 'CFE%'
                    AND ts >= @inicio AND ts < @fin
                  ORDER BY id_asignado, ts",
                new { inicio, fin });

            var porEstacion = new Dictionary<string, List<HoraLluvia>>();
            foreach (var r in rows)
            {
                if (!porEstacion.ContainsKey(r.id_asignado))
                    porEstacion[r.id_asignado] = new();

                var local = r.ts.ToOffset(UtcMinus6);
                porEstacion[r.id_asignado].Add(new HoraLluvia
                {
                    HoraUtc = r.ts.ToString("o"),
                    HoraLocal = local.ToString("yyyy-MM-dd HH:mm"),
                    AcumuladoMm = Math.Round(r.acumulado, 2),
                });
            }

            var result = new List<EstacionHorario>();
            foreach (var (idA, horas) in porEstacion)
            {
                meta.TryGetValue(idA, out var m);
                result.Add(new EstacionHorario
                {
                    IdAsignado = idA,
                    Nombre = m?.Nombre ?? idA,
                    Cuenca = m?.Cuenca ?? "",
                    Horas = horas,
                    TotalMm = Math.Round(horas.Sum(h => h.AcumuladoMm), 2),
                });
            }
            result.Sort((a, b) => b.TotalMm.CompareTo(a.TotalMm));

            return Ok(new
            {
                tipo,
                periodoInicio = inicio.ToString("o"),
                periodoFin = fin.ToString("o"),
                generado = ahora.ToString("o"),
                estaciones = result,
            });
        }

        /// <summary>
        /// Precipitación agrupada solo por cuenca (resumen ejecutivo).
        /// </summary>
        [HttpGet("cuencas")]
        public async Task<IActionResult> ReporteCuencas(
            [FromQuery] string tipo = "24h",
            [FromQuery] string? fecha = null)
        {
            var (ayer6am, hoy6am, ahora) = CalcularPeriodos(fecha);
            DateTimeOffset inicio, fin;
            if (tipo == "24h")
            { inicio = ayer6am; fin = hoy6am; }
            else
            { inicio = hoy6am; fin = ahora; }

            var estaciones = await ConsultarAcumulado(inicio, fin);
            var cuencas = AgruparPorCuenca(estaciones);

            return Ok(new
            {
                tipoPeriodo = tipo,
                periodoInicio = inicio.ToString("o"),
                periodoFin = fin.ToString("o"),
                generado = ahora.ToString("o"),
                cuencas,
            });
        }

        // ── Lógica interna ──────────────────────────────────────────

        /// <summary>
        /// Reporte de precipitación agrupado por subcuenca con promedio (estilo CFE).
        /// tipo: "24h" o "parcial"
        /// </summary>
        [HttpGet("reporte")]
        public async Task<IActionResult> ReporteSubcuenca(
            [FromQuery] string tipo = "parcial",
            [FromQuery] string? fecha = null)
        {
            var (ayer6am, hoy6am, ahora) = CalcularPeriodos(fecha);
            DateTimeOffset inicio, fin;
            string titulo;
            if (tipo == "24h")
            {
                inicio = ayer6am; fin = hoy6am;
                var inicioLocal = inicio.ToOffset(UtcMinus6);
                var finLocal = fin.ToOffset(UtcMinus6);
                titulo = $"Reporte de precipitación 24 horas";
            }
            else
            {
                inicio = hoy6am; fin = ahora;
                titulo = $"Reporte parcial de precipitación";
            }

            var estaciones = await ConsultarAcumulado(inicio, fin);
            // Filtrar solo las que tienen acumulado > 0
            var conLluvia = estaciones.Where(e => e.AcumuladoMm > 0).ToList();

            var subcuencas = conLluvia
                .GroupBy(e => string.IsNullOrEmpty(e.Subcuenca) ? "Sin subcuenca" : e.Subcuenca)
                .OrderBy(g => g.Key)
                .Select(g => new SubcuencaReporte
                {
                    Subcuenca = g.Key,
                    Estaciones = g.OrderBy(e => e.Nombre).ToList(),
                    PromedioMm = Math.Round(g.Average(e => e.AcumuladoMm), 1)
                })
                .ToList();

            var inicioL = inicio.ToOffset(UtcMinus6);
            var finL = fin.ToOffset(UtcMinus6);

            return Ok(new
            {
                titulo,
                tipo,
                periodoInicio = inicio.ToString("o"),
                periodoFin = fin.ToString("o"),
                periodoInicioLocal = inicioL.ToString("dd/MM/yyyy HH:mm"),
                periodoFinLocal = finL.ToString("dd/MM/yyyy HH:mm"),
                generado = ahora.ToString("o"),
                totalEstaciones = estaciones.Count,
                estacionesConLluvia = conLluvia.Count,
                subcuencas,
                cuencas = AgruparPorCuenca(estaciones)
            });
        }

        // ── Lógica interna original ─────────────────────────────────

        private (DateTimeOffset ayer6am, DateTimeOffset hoy6am, DateTimeOffset ahora) CalcularPeriodos(string? fechaStr)
        {
            var ahora = DateTimeOffset.UtcNow;

            DateTimeOffset hoy6am;
            if (!string.IsNullOrEmpty(fechaStr) && DateOnly.TryParse(fechaStr, out var fechaDt))
            {
                // Interpretar fecha como día local UTC-6
                hoy6am = new DateTimeOffset(fechaDt.Year, fechaDt.Month, fechaDt.Day, 6, 0, 0,
                    UtcMinus6).ToUniversalTime();
            }
            else
            {
                // 6:00 AM local = 12:00 PM UTC
                var hoy12Utc = new DateTimeOffset(ahora.UtcDateTime.Date.AddHours(12), TimeSpan.Zero);
                hoy6am = ahora < hoy12Utc ? hoy12Utc.AddDays(-1) : hoy12Utc;
            }

            var ayer6am = hoy6am.AddDays(-1);
            return (ayer6am, hoy6am, ahora);
        }

        private async Task<List<EstacionLluvia>> ConsultarAcumulado(DateTimeOffset inicio, DateTimeOffset fin)
        {
            var meta = await CargarMetaCfe();

            using var pg = new NpgsqlConnection(_pgConn);
            var rows = await pg.QueryAsync(
                @"SELECT id_asignado, dcp_id,
                         COALESCE(SUM(acumulado), 0) AS total_mm,
                         COUNT(*) AS horas
                  FROM resumen_horario
                  WHERE variable = 'precipitación'
                    AND id_asignado LIKE 'CFE%'
                    AND ts >= @inicio AND ts < @fin
                  GROUP BY id_asignado, dcp_id
                  ORDER BY total_mm DESC",
                new { inicio, fin });

            var result = new List<EstacionLluvia>();
            foreach (var r in rows)
            {
                string idA = r.id_asignado;
                meta.TryGetValue(idA, out var m);
                result.Add(new EstacionLluvia
                {
                    IdAsignado = idA,
                    DcpId = r.dcp_id ?? "",
                    Nombre = m?.Nombre ?? idA,
                    Cuenca = m?.Cuenca ?? "",
                    Subcuenca = m?.Subcuenca ?? "",
                    AcumuladoMm = Math.Round((double)r.total_mm, 2),
                    HorasConDato = (int)(long)r.horas,
                });
            }
            return result;
        }

        private record EstacionMeta(string Nombre, string Cuenca, string Subcuenca);

        private async Task<Dictionary<string, EstacionMeta>> CargarMetaCfe()
        {
            using var sql = new SqlConnection(_sqlConn);
            var rows = await sql.QueryAsync(
                @"SELECT g.IdAsignado, g.Nombre,
                         ISNULL(c.Nombre, '') AS Cuenca,
                         ISNULL(sc.Nombre, '') AS Subcuenca
                  FROM [dbo].[NV_GoesSGD] g
                  LEFT JOIN Estacion e ON g.IdAsignado = e.IdAsignado
                  LEFT JOIN Cuenca c ON e.IdCuenca = c.Id
                  LEFT JOIN Subcuenca sc ON e.IdSubcuenca = sc.Id
                  WHERE g.IdAsignado LIKE 'CFE%'");

            var dict = new Dictionary<string, EstacionMeta>();
            foreach (var r in rows)
            {
                string idA = ((string)r.IdAsignado).Trim();
                dict[idA] = new EstacionMeta(
                    ((string)r.Nombre).Trim(),
                    ((string)r.Cuenca).Trim(),
                    ((string)r.Subcuenca).Trim()
                );
            }
            return dict;
        }

        private List<CuencaLluvia> AgruparPorCuenca(List<EstacionLluvia> estaciones)
        {
            // Agrupar por cuenca
            var grupos = estaciones
                .Where(e => !string.IsNullOrEmpty(e.Cuenca))
                .GroupBy(e => e.Cuenca)
                .Select(g =>
                {
                    var vals = g.Select(e => e.AcumuladoMm).ToList();
                    var prom = vals.Average();
                    return new CuencaLluvia
                    {
                        Nombre = g.Key,
                        Tipo = "cuenca",
                        PromedioMm = Math.Round(prom, 2),
                        MaxMm = Math.Round(vals.Max(), 2),
                        MinMm = Math.Round(vals.Min(), 2),
                        EstacionesConDato = vals.Count,
                        EstacionesTotal = vals.Count,
                        Semaforo = prom < 2.5 ? "verde" : prom < 7.5 ? "amarillo" : prom < 15 ? "naranja" : "rojo",
                    };
                })
                .OrderByDescending(c => c.PromedioMm)
                .ToList();

            return grupos;
        }
    }
}
