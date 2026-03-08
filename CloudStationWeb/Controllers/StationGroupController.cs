using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using CloudStationWeb.Services;
using CloudStationWeb.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloudStationWeb.Controllers
{
    [Authorize]
    public class StationGroupController : Controller
    {
        private readonly DataService _dataService;

        public StationGroupController(DataService dataService)
        {
            _dataService = dataService;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetGroups()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                var groups = await _dataService.GetStationGroupsAsync(userId);
                return Json(groups);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateGroup([FromBody] GroupCreationRequest request)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                if (string.IsNullOrWhiteSpace(request?.Name))
                {
                    return BadRequest(new { error = "El nombre del grupo es requerido." });
                }

                var newId = await _dataService.CreateStationGroupAsync(request.Name, userId);
                return Json(new { success = true, id = newId });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteGroup(int id)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                var success = await _dataService.DeleteStationGroupAsync(id, userId);
                if (!success) return NotFound(new { error = "Grupo no encontrado o no autorizado." });

                return Json(new { success = true });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetGroupStations(int id)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                var stations = await _dataService.GetGroupStationsAsync(id, userId);
                return Json(stations);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateGroupStations(int id, [FromBody] List<string> stationIds)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                var obj = stationIds ?? new List<string>();

                var success = await _dataService.UpdateGroupStationsAsync(id, userId, obj);
                if (!success) return NotFound(new { error = "Grupo no encontrado o no autorizado." });

                return Json(new { success = true });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class GroupCreationRequest
    {
        public string Name { get; set; } = "";
    }
}
