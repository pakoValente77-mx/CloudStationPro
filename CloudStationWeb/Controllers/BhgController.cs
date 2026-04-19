using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CloudStationWeb.Services;
using CloudStationWeb.Controllers;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CloudStationWeb.Controllers
{
    [Authorize(AuthenticationSchemes = "Identity.Application,Bearer")]
    public class BhgController : Controller
    {
        private readonly BhgService _service;
        private readonly ILogger<BhgController> _logger;
        private readonly string _sqlConn;

        public BhgController(BhgService service, ILogger<BhgController> logger, IConfiguration config)
        {
            _service = service;
            _logger = logger;
            _sqlConn = config.GetConnectionString("SqlServer") ?? "";
        }

        public async Task<IActionResult> Index(int? mes = null, int? anio = null)
        {
            var vm = await _service.GetDataAsync(mes, anio);

            // Load active embalses for dynamic rendering
            try
            {
                using var db = new SqlConnection(_sqlConn);
                var configs = (await db.QueryAsync<EmbalseConfigDto>(
                    "SELECT Id, PresaKey, NombreDisplay, NAMO, NAME, NAMINO, IsActive, SortOrder, Color, HydroKey, CuencaCode, ExcelHeaderRow, ExcelDataStartRow, ExcelDataEndRow, IsTaponType, TotalUnits, BhgKey FROM EmbalseConfig WHERE IsActive = 1 ORDER BY SortOrder")).ToList();
                ViewBag.EmbalseConfigs = configs;
            }
            catch { ViewBag.EmbalseConfigs = new List<EmbalseConfigDto>(); }

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> GetData(int? mes = null, int? anio = null)
        {
            var vm = await _service.GetDataAsync(mes, anio);
            return Json(vm);
        }
    }
}
