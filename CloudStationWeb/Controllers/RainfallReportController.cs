using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloudStationWeb.Controllers
{
    [Authorize]
    public class RainfallReportController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
