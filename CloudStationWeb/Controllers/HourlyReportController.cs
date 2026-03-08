using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CloudStationWeb.Services;
using CloudStationWeb.Models;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Security.Claims;

namespace CloudStationWeb.Controllers
{
    [Authorize]
    public class HourlyReportController : Controller
    {
        private readonly DataService _dataService;

        public HourlyReportController(DataService dataService)
        {
            _dataService = dataService;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetHourlyReport(string variable = "precipitación", int startHour = 6, bool onlyCfe = true, string? date = null, int? groupId = null)
        {
            try
            {
                DateTime? targetDate = null;
                if (!string.IsNullOrEmpty(date))
                {
                    if (DateTime.TryParse(date, out DateTime parsedDate))
                    {
                        targetDate = parsedDate;
                    }
                }
                
                var report = await _dataService.GetHourlyReportAsync(variable, startHour, onlyCfe, targetDate, groupId);
                return Json(report);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportToExcel(string variable = "precipitación", int startHour = 6, bool onlyCfe = true, bool grouped = false, string? date = null, int? groupId = null)
        {
            try
            {
                DateTime? targetDate = null;
                if (!string.IsNullOrEmpty(date))
                {
                    if (DateTime.TryParse(date, out DateTime parsedDate))
                    {
                        targetDate = parsedDate;
                    }
                }
                var report = await _dataService.GetHourlyReportAsync(variable, startHour, onlyCfe, targetDate, groupId);
                
                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Reporte Horario");
                    
                    int currentRow = 1;
                    
                    // Title
                    worksheet.Cells[currentRow, 1].Value = $"Reporte Horario - {variable}";
                    worksheet.Cells[currentRow, 1].Style.Font.Size = 16;
                    worksheet.Cells[currentRow, 1].Style.Font.Bold = true;
                    currentRow += 2;
                    
                    // Period info
                    var startTime = DateTime.Parse(report.StartTime);
                    var endTime = DateTime.Parse(report.EndTime);
                    worksheet.Cells[currentRow, 1].Value = $"Período: {startTime:dd/MM/yyyy HH:mm} - {endTime:dd/MM/yyyy HH:mm}";
                    currentRow++;
                    worksheet.Cells[currentRow, 1].Value = $"Estaciones: {report.Stations.Count}";
                    currentRow += 2;
                    
                    // Headers
                    int col = 1;
                    worksheet.Cells[currentRow, col++].Value = "Estación";
                    
                    // Hour headers
                    for (int i = 1; i <= 24; i++)
                    {
                        var hourTime = startTime.AddHours(i);
                        worksheet.Cells[currentRow, col++].Value = hourTime.ToString("HH:mm");
                    }
                    
                    // Style header row
                    using (var range = worksheet.Cells[currentRow, 1, currentRow, col - 1])
                    {
                        range.Style.Font.Bold = true;
                        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(59, 130, 246));
                        range.Style.Font.Color.SetColor(System.Drawing.Color.White);
                        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    }
                    currentRow++;
                    
                    // Data rows
                    if (grouped)
                    {
                        // Group by Cuenca and Subcuenca
                        var groupedStations = report.Stations
                            .GroupBy(s => s.Cuenca ?? "Sin Cuenca")
                            .OrderBy(g => g.Key);
                        
                        foreach (var cuencaGroup in groupedStations)
                        {
                            // Cuenca header
                            worksheet.Cells[currentRow, 1].Value = cuencaGroup.Key;
                            using (var range = worksheet.Cells[currentRow, 1, currentRow, 25])
                            {
                                range.Merge = true;
                                range.Style.Font.Bold = true;
                                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(30, 64, 175));
                                range.Style.Font.Color.SetColor(System.Drawing.Color.White);
                            }
                            currentRow++;
                            
                            var subcuencaGroups = cuencaGroup
                                .GroupBy(s => s.Subcuenca ?? "Sin Subcuenca")
                                .OrderBy(g => g.Key);
                            
                            foreach (var subcuencaGroup in subcuencaGroups)
                            {
                                // Subcuenca header
                                worksheet.Cells[currentRow, 1].Value = "  " + subcuencaGroup.Key;
                                using (var range = worksheet.Cells[currentRow, 1, currentRow, 25])
                                {
                                    range.Merge = true;
                                    range.Style.Font.Bold = true;
                                    range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(59, 130, 246));
                                    range.Style.Font.Color.SetColor(System.Drawing.Color.White);
                                }
                                currentRow++;
                                
                                // Stations in subcuenca
                                foreach (var station in subcuencaGroup)
                                {
                                    AddStationRow(worksheet, currentRow++, station, startTime, variable);
                                }
                            }
                        }
                    }
                    else
                    {
                        // Flat list
                        foreach (var station in report.Stations)
                        {
                            AddStationRow(worksheet, currentRow++, station, startTime, variable);
                        }
                    }
                    
                    // Auto-fit columns
                    worksheet.Cells.AutoFitColumns();
                    worksheet.Column(1).Width = 30; // Station name column wider
                    
                    // Freeze header row
                    worksheet.View.FreezePanes(6, 2);
                    
                    var stream = new MemoryStream();
                    package.SaveAs(stream);
                    stream.Position = 0;
                    
                    var fileName = $"ReporteHorario_{variable}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                    return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error exporting to Excel: {ex}");
                return StatusCode(500, new { error = ex.Message });
            }
        }
        
        private void AddStationRow(ExcelWorksheet worksheet, int row, HourlyReportData station, DateTime startTime, string variable)
        {
            int col = 1;
            worksheet.Cells[row, col++].Value = station.StationName;
            
            // Create value map
            var valueMap = new Dictionary<int, HourlyValue>();
            foreach (var hv in station.HourlyValues)
            {
                var hourDateTime = DateTime.Parse(hv.Hour);
                valueMap[hourDateTime.Hour] = hv;
            }
            
            // Fill 24 hours
            for (int i = 1; i <= 24; i++)
            {
                var hourTime = startTime.AddHours(i);
                var hour = hourTime.Hour;
                
                if (valueMap.TryGetValue(hour, out var hv) && hv.Value.HasValue)
                {
                    var value = (double)hv.Value.Value;
                    worksheet.Cells[row, col].Value = value;
                    bool isPrecipitation = variable == "precipitación";
                    worksheet.Cells[row, col].Style.Numberformat.Format = isPrecipitation ? "0.0" : "0.00";
                    
                    if (!hv.IsValid)
                    {
                        worksheet.Cells[row, col].Style.Font.Color.SetColor(System.Drawing.Color.Red);
                        worksheet.Cells[row, col].Style.Font.Strike = true;
                    }
                    else if (isPrecipitation && value > 0)
                    {
                        // Color coding for precipitation
                        var color = value switch
                        {
                            >= 50 => System.Drawing.Color.FromArgb(127, 0, 255),
                            >= 25 => System.Drawing.Color.FromArgb(255, 0, 0),
                            >= 10 => System.Drawing.Color.FromArgb(255, 127, 0),
                            >= 5 => System.Drawing.Color.FromArgb(255, 255, 0),
                            >= 2.5 => System.Drawing.Color.FromArgb(144, 238, 144),
                            _ => System.Drawing.Color.FromArgb(173, 216, 230)
                        };
                        worksheet.Cells[row, col].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[row, col].Style.Fill.BackgroundColor.SetColor(color);
                    }
                }
                else
                {
                    worksheet.Cells[row, col].Value = "-";
                    worksheet.Cells[row, col].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                }
                col++;
            }
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
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> DebugSchema()
        {
            try
            {
                var schema = await _dataService.GetTableSchemaAsync();
                return Json(schema);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, stack = ex.StackTrace });
            }
        }
    }
}
