using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CloudStationWeb.Models;
using CloudStationWeb.Services;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CloudStationWeb.Controllers
{
    [Authorize(Roles = "SuperAdmin,Administrador")]
    public class EarlyWarningController : Controller
    {
        private readonly IConfiguration _config;
        private readonly EarlyWarningService _earlyWarning;
        private readonly string _sqlConn;

        public EarlyWarningController(
            IConfiguration config,
            EarlyWarningService earlyWarning)
        {
            _config = config;
            _earlyWarning = earlyWarning;
            _sqlConn = config.GetConnectionString("SqlServer")!;
        }

        // GET: /EarlyWarning
        public IActionResult Index()
        {
            var cfg = _earlyWarning.GetConfig();
            ViewBag.Enabled = cfg.Enabled;
            ViewBag.IntervalSeconds = cfg.IntervalSeconds;
            ViewBag.CooldownMinutes = cfg.CooldownMinutes;
            return View();
        }

        // GET: /EarlyWarning/GetAlertHistory
        [HttpGet]
        public async Task<IActionResult> GetAlertHistory(int days = 7)
        {
            using var db = new SqlConnection(_sqlConn);
            var alerts = await db.QueryAsync<AlertRecord>(@"
                SELECT TOP 200 Id, IdSensor, IdUmbral, NombreEstacion, NombreSensor, NombreUmbral,
                       Variable, ValorMedido, ValorUmbral, Operador, Nivel,
                       FechaAlerta, FechaEnvio, CorreosEnviados, Enviada
                FROM AlertRecord
                WHERE FechaAlerta >= DATEADD(DAY, -@Days, GETUTCDATE())
                ORDER BY FechaAlerta DESC", new { Days = days });
            return Json(alerts);
        }

        // GET: /EarlyWarning/GetActiveThresholds
        [HttpGet]
        public async Task<IActionResult> GetActiveThresholds()
        {
            using var db = new SqlConnection(_sqlConn);
            var thresholds = await db.QueryAsync<UmbralConContexto>(@"
                SELECT u.Id, u.IdSensor, u.Umbral, u.Operador, u.Nombre, u.Activo, u.Periodo,
                       s.Nombre AS NombreSensor, s.Variable,
                       e.Nombre AS NombreEstacion, e.IdSatelital AS DcpId
                FROM UmbralAlertas u
                INNER JOIN Sensor s ON u.IdSensor = s.Id
                INNER JOIN Estacion e ON s.IdEstacion = e.Id
                WHERE u.Activo = 1 AND e.Activo = 1
                ORDER BY e.Nombre, s.Nombre");
            return Json(thresholds);
        }

        // POST: /EarlyWarning/UpdateConfig
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateConfig([FromBody] EarlyWarningConfigDto dto)
        {
            _earlyWarning.UpdateConfig(dto.Enabled, dto.IntervalSeconds, dto.CooldownMinutes);
            return Json(new { success = true });
        }

        // GET: /EarlyWarning/GetStats
        [HttpGet]
        public async Task<IActionResult> GetStats()
        {
            using var db = new SqlConnection(_sqlConn);
            var stats = await db.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT 
                    (SELECT COUNT(*) FROM AlertRecord WHERE FechaAlerta >= DATEADD(HOUR, -24, GETUTCDATE())) AS Last24h,
                    (SELECT COUNT(*) FROM AlertRecord WHERE FechaAlerta >= DATEADD(DAY, -7, GETUTCDATE())) AS Last7d,
                    (SELECT COUNT(*) FROM AlertRecord WHERE Nivel = 'CRÍTICA' AND FechaAlerta >= DATEADD(DAY, -7, GETUTCDATE())) AS CriticasLast7d,
                    (SELECT COUNT(*) FROM UmbralAlertas WHERE Activo = 1) AS UmbralesActivos
            ") ?? new { Last24h = 0, Last7d = 0, CriticasLast7d = 0, UmbralesActivos = 0 };
            return Json(stats);
        }
    }
}
