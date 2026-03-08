using System.Threading.Tasks;
using CloudStationWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloudStationWeb.Controllers
{
    [Authorize]
    public class MapController : Controller
    {
        private readonly DataService _dataService;

        public MapController(DataService dataService)
        {
            _dataService = dataService;
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
    }
}
