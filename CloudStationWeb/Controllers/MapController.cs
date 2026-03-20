using System.Threading.Tasks;
using CloudStationWeb.Models;
using CloudStationWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace CloudStationWeb.Controllers
{
    [Authorize]
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
            return Json(history);
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
            return Json(data);
        }

        [HttpGet]
        public async Task<JsonResult> GetCuencaEstaciones(string code, bool onlyCfe = true)
        {
            var data = await _dataService.GetCuencaEstacionesAsync(code, onlyCfe);
            return Json(data);
        }

        [HttpGet]
        public JsonResult GetCuencasKml()
        {
            var config = _configuration.GetSection("CuencasKml").Get<List<CuencaKmlConfig>>() ?? new();
            return Json(config.Select(c => new { code = c.Code, label = c.Label, kmlFile = c.KmlFile, color = c.Color }));
        }
    }
}
