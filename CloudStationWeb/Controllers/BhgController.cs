using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CloudStationWeb.Services;

namespace CloudStationWeb.Controllers
{
    [Authorize(AuthenticationSchemes = "Identity.Application,Bearer")]
    public class BhgController : Controller
    {
        private readonly BhgService _service;
        private readonly ILogger<BhgController> _logger;

        public BhgController(BhgService service, ILogger<BhgController> logger)
        {
            _service = service;
            _logger = logger;
        }

        public async Task<IActionResult> Index(int? mes = null, int? anio = null)
        {
            var vm = await _service.GetDataAsync(mes, anio);
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
