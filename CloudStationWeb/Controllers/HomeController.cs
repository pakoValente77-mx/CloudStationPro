using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using CloudStationWeb.Models;

namespace CloudStationWeb.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    /// <summary>
    /// Lightweight endpoint to keep the authentication cookie alive.
    /// Called periodically by the global keepalive script in _Layout.
    /// </summary>
    [HttpGet]
    public IActionResult KeepAlive()
    {
        return Json(new { alive = true, ts = DateTime.UtcNow });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
