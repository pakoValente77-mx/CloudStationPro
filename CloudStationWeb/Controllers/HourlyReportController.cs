using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CloudStationWeb.Services;
using CloudStationWeb.Models;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using OfficeOpenXml.Drawing.Chart;
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
                    var startTime = DateTime.Parse(report.StartTime);
                    var endTime = DateTime.Parse(report.EndTime);
                    bool isPrecipitation = variable.ToLower().Contains("precipitación");
                    
                    if (isPrecipitation)
                    {
                        BuildPrecipitationSheet(package, report, startTime, endTime);
                        BuildReporteLluviaSheet(package, report, startTime);
                    }
                    else
                    {
                        BuildGenericSheet(package, report, variable, startTime, grouped);
                    }
                    
                    var stream = new MemoryStream();
                    package.SaveAs(stream);
                    stream.Position = 0;
                    
                    var fileName = isPrecipitation
                        ? $"NRED{endTime:ddMMyy}.xlsx"
                        : $"ReporteHorario_{variable}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                    return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error exporting to Excel: {ex}");
                return StatusCode(500, new { error = ex.Message });
            }
        }
        
        // ============================================================
        //  Grouping by DB Cuenca / Subcuenca (no hardcoded mapping)
        // ============================================================
        
        /// Build date+hour key to avoid cross-day collisions in valueMap
        private static string DtKey(DateTime dt) => $"{dt:yyyy-MM-dd}-{dt.Hour}";
        
        private static Dictionary<string, HourlyValue> BuildValueMap(HourlyReportData station)
        {
            var map = new Dictionary<string, HourlyValue>();
            foreach (var hv in station.HourlyValues)
            {
                var dt = DateTime.Parse(hv.Hour);
                map[DtKey(dt)] = hv;
            }
            return map;
        }
        
        // ============================================================
        //  PRECIPITACIÓN – Hoja principal estilo CFE / NRED
        // ============================================================
        private void BuildPrecipitationSheet(ExcelPackage package, HourlyReportResponse report, DateTime startTime, DateTime endTime)
        {
            var ws = package.Workbook.Worksheets.Add("Precipitación");
            
            // Colors
            var darkGreen  = System.Drawing.Color.FromArgb(27, 94, 32);
            var medGreen   = System.Drawing.Color.FromArgb(46, 125, 50);
            var lightGreen = System.Drawing.Color.FromArgb(76, 175, 80);
            var paleGreen  = System.Drawing.Color.FromArgb(232, 245, 233);
            var gold       = System.Drawing.Color.FromArgb(194, 147, 28);
            var white      = System.Drawing.Color.White;
            
            // Layout: Col1=No, Col2=Estación, Col3‑26=24 horas, Col27=TOTAL
            const int hourStartCol = 3;
            const int totalCol = 27;
            const int lastCol = 27;
            
            int row = 1;
            
            // ── Row 1: Title ──
            ws.Cells[row, 1, row, lastCol].Merge = true;
            SetCell(ws, row, 1, "DEPARTAMENTO REGIONAL DE HIDROMETRÍA", "Arial", 16, true, white, darkGreen, ExcelHorizontalAlignment.Center);
            ws.Row(row).Height = 28;
            row++;
            
            // ── Row 2: Subtitle ──
            ws.Cells[row, 1, row, lastCol].Merge = true;
            SetCell(ws, row, 1, "PUESTO CENTRAL DE REGISTRO", "Arial", 12, true, white, darkGreen, ExcelHorizontalAlignment.Center);
            row++;
            
            // ── Row 3: Description ──
            ws.Cells[row, 1, row, lastCol].Merge = true;
            SetCell(ws, row, 1, "Reporte de la Información Hidrometeorológica Automática", "Arial", 12, true, white, medGreen, ExcelHorizontalAlignment.Center);
            row++;
            
            // ── Row 4: Report type + Date ──
            ws.Cells[row, 1, row, 20].Merge = true;
            SetCell(ws, row, 1, "REPORTE DE PRECIPITACIONES", "Arial", 12, true, white, medGreen, ExcelHorizontalAlignment.Center);
            ws.Cells[row, 21, row, lastCol].Merge = true;
            SetCell(ws, row, 21, $"FECHA: {endTime:dd/MM/yyyy}     Precipitación (mm)", "Arial", 10, true, white, medGreen, ExcelHorizontalAlignment.Center);
            row++;
            
            // ── Row 5: Column headers ──
            ws.Cells[row, 1].Value = "No.";
            ws.Cells[row, 2].Value = "ESTACIÓN";
            for (int i = 1; i <= 24; i++)
            {
                var ht = startTime.AddHours(i);
                int h = ht.Hour == 0 ? 24 : ht.Hour;
                ws.Cells[row, i + 2].Value = h;
            }
            ws.Cells[row, totalCol].Value = "TOTAL";
            using (var range = ws.Cells[row, 1, row, lastCol])
            {
                range.Style.Font.Bold = true;
                range.Style.Font.Name = "Arial";
                range.Style.Font.Size = 10;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(lightGreen);
                range.Style.Font.Color.SetColor(white);
                range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
                range.Style.Border.Bottom.Color.SetColor(darkGreen);
            }
            ws.Cells[row, totalCol].Style.Fill.BackgroundColor.SetColor(gold);
            ws.Cells[row, totalCol].Style.Font.Color.SetColor(System.Drawing.Color.Black);
            int headerRow = row;
            row++;
            
            // ── Data by cuenca (from DB) ──
            var cuencaGroups = report.Stations
                .GroupBy(s => string.IsNullOrWhiteSpace(s.Cuenca) || s.Cuenca.Equals("Indefinida", StringComparison.OrdinalIgnoreCase)
                    ? "OTRAS ESTACIONES" : s.Cuenca)
                .OrderBy(g => g.Key == "OTRAS ESTACIONES" ? 1 : 0)
                .ThenBy(g => g.Key);
            int stationNum = 1;
            
            foreach (var cuencaGroup in cuencaGroups)
            {
                string cuencaLabel = cuencaGroup.Key;
                
                // Sub-group by subcuenca within this cuenca
                var subGroups = cuencaGroup
                    .GroupBy(s => string.IsNullOrWhiteSpace(s.Subcuenca) || s.Subcuenca.Equals("Indefinida", StringComparison.OrdinalIgnoreCase)
                        ? "" : s.Subcuenca)
                    .OrderBy(g => g.Key);
                
                foreach (var subGroup in subGroups)
                {
                    // Header row
                    string header = string.IsNullOrEmpty(subGroup.Key)
                        ? $"CUENCA {cuencaLabel.ToUpper()}"
                        : $"SUBCUENCA {subGroup.Key.ToUpper()} — {cuencaLabel.ToUpper()}";
                    
                    ws.Cells[row, 1, row, lastCol].Merge = true;
                    SetCell(ws, row, 1, header, "Arial", 10, true,
                        darkGreen, System.Drawing.Color.FromArgb(200, 230, 201), ExcelHorizontalAlignment.Left);
                    row++;
                    
                    var stations = subGroup.OrderBy(s => s.StationName).ToList();
                    
                    // Hourly sums for group average
                    double[] hourSums = new double[24];
                    int[] hourCounts = new int[24];
                    
                    foreach (var station in stations)
                    {
                        bool isMaint = station.EnMantenimiento;
                        ws.Cells[row, 1].Value = stationNum++;
                        ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        ws.Cells[row, 2].Value = isMaint ? $"{station.StationName} [MTTO]" : station.StationName;
                        ws.Cells[row, 2].Style.Font.Size = 9;
                        if (isMaint)
                        {
                            ws.Cells[row, 2].Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(249, 115, 22));
                            ws.Cells[row, 2].Style.Font.Italic = true;
                        }
                        
                        var vmap = BuildValueMap(station);
                        double rowTotal = 0;
                        bool hasAny = false;
                        
                        for (int i = 1; i <= 24; i++)
                        {
                            var ht = startTime.AddHours(i);
                            int col = i + 2;
                            
                            if (vmap.TryGetValue(DtKey(ht), out var hv) && hv.Value.HasValue)
                            {
                                double val = (double)hv.Value.Value;
                                ws.Cells[row, col].Value = val;
                                ws.Cells[row, col].Style.Numberformat.Format = "0.0";
                                ws.Cells[row, col].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                                
                                if (hv.IsValid && !isMaint)
                                {
                                    rowTotal += val;
                                    hourSums[i - 1] += val;
                                    hourCounts[i - 1]++;
                                    ApplyPrecipColor(ws.Cells[row, col], val);
                                }
                                else
                                {
                                    ws.Cells[row, col].Style.Font.Color.SetColor(System.Drawing.Color.Red);
                                    ws.Cells[row, col].Style.Font.Strike = true;
                                }
                                hasAny = true;
                            }
                            else
                            {
                                ws.Cells[row, col].Value = "-";
                                ws.Cells[row, col].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                                ws.Cells[row, col].Style.Font.Color.SetColor(System.Drawing.Color.Gray);
                            }
                        }
                        
                        // Total
                        if (hasAny)
                        {
                            ws.Cells[row, totalCol].Value = rowTotal;
                            ws.Cells[row, totalCol].Style.Numberformat.Format = "0.0";
                            ws.Cells[row, totalCol].Style.Font.Bold = true;
                            ws.Cells[row, totalCol].Style.Fill.PatternType = ExcelFillStyle.Solid;
                            ws.Cells[row, totalCol].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(255, 248, 225));
                        }
                        else
                        {
                            ws.Cells[row, totalCol].Value = "-";
                            ws.Cells[row, totalCol].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        }
                        
                        // Thin bottom border for station row
                        using (var range = ws.Cells[row, 1, row, lastCol])
                        {
                            range.Style.Border.Bottom.Style = ExcelBorderStyle.Hair;
                            range.Style.Border.Bottom.Color.SetColor(System.Drawing.Color.FromArgb(200, 200, 200));
                        }
                        row++;
                    }
                    
                    // ── Average row ──
                    ws.Cells[row, 2].Value = "Precipitación media horaria";
                    ws.Cells[row, 2].Style.Font.Italic = true;
                    ws.Cells[row, 2].Style.Font.Size = 9;
                    using (var range = ws.Cells[row, 1, row, lastCol])
                    {
                        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        range.Style.Fill.BackgroundColor.SetColor(paleGreen);
                        range.Style.Font.Bold = true;
                        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Top.Color.SetColor(medGreen);
                        range.Style.Border.Bottom.Color.SetColor(medGreen);
                    }
                    double avgTotal = 0;
                    for (int i = 0; i < 24; i++)
                    {
                        int col = i + hourStartCol;
                        if (hourCounts[i] > 0)
                        {
                            double avg = hourSums[i] / hourCounts[i];
                            ws.Cells[row, col].Value = avg;
                            ws.Cells[row, col].Style.Numberformat.Format = "0.0";
                            ws.Cells[row, col].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                            avgTotal += avg;
                        }
                        else
                        {
                            ws.Cells[row, col].Value = "-";
                            ws.Cells[row, col].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        }
                    }
                    ws.Cells[row, totalCol].Value = avgTotal;
                    ws.Cells[row, totalCol].Style.Numberformat.Format = "0.0";
                    ws.Cells[row, totalCol].Style.Font.Bold = true;
                    row += 2; // blank separator row
                }
            }
            
            // ── Hyetograph data rows (hidden, used as chart source) ──
            int hyetoDataRow = row;
            {
                // Compute global hourly TOTAL across ALL stations
                var allMaps = report.Stations.Select(s => BuildValueMap(s)).ToList();
                for (int i = 1; i <= 24; i++)
                {
                    var ht = startTime.AddHours(i);
                    int h = ht.Hour == 0 ? 24 : ht.Hour;
                    ws.Cells[hyetoDataRow, i + 2].Value = h; // hour label
                    
                    double sum = 0;
                    foreach (var vmap in allMaps)
                    {
                        if (vmap.TryGetValue(DtKey(ht), out var hv) && hv.Value.HasValue && hv.IsValid)
                        {
                            sum += (double)hv.Value.Value;
                        }
                    }
                    ws.Cells[hyetoDataRow + 1, i + 2].Value = sum;
                    ws.Cells[hyetoDataRow + 1, i + 2].Style.Numberformat.Format = "0.0";
                }
                // Labels
                ws.Cells[hyetoDataRow, 2].Value = "Hora";
                ws.Cells[hyetoDataRow + 1, 2].Value = "Precip. total (mm)";
                ws.Row(hyetoDataRow).Hidden = true;
                ws.Row(hyetoDataRow + 1).Hidden = true;
                row = hyetoDataRow + 3;
            }
            
            // ── Hyetograph chart ──
            {
                var chart = ws.Drawings.AddChart("Hietograma", eChartType.ColumnClustered);
                chart.Title.Text = "Hietograma General — Precipitación Total por Hora (mm)";
                chart.Title.Font.Size = 12;
                chart.Title.Font.Bold = true;
                
                // Data: hyetoDataRow+1 cols 3..26 = values; hyetoDataRow cols 3..26 = hour labels
                var dataSeries = chart.Series.Add(
                    ws.Cells[hyetoDataRow + 1, hourStartCol, hyetoDataRow + 1, 26],  // values
                    ws.Cells[hyetoDataRow, hourStartCol, hyetoDataRow, 26]);         // categories (hours)
                dataSeries.Header = "Precipitación total (mm)";
                
                // Style bars with green
                dataSeries.Fill.Style = OfficeOpenXml.Drawing.eFillStyle.SolidFill;
                dataSeries.Fill.SolidFill.Color.SetRgbColor(System.Drawing.Color.FromArgb(46, 125, 50));
                
                // Chart size and position
                chart.SetPosition(row - 1, 0, 1, 0);
                chart.SetSize(1400, 350);
                chart.Style = eChartStyle.Style2;
                
                // Y axis
                chart.YAxis.Title.Text = "mm";
                chart.YAxis.MinValue = 0;
                
                // X axis
                chart.XAxis.Title.Text = "Hora";
                
                // Legend off (only one series)
                chart.Legend.Remove();
                
                row += 22; // space for chart (~22 rows)
            }
            
            // ── Notes ──
            ws.Cells[row, 1].Value = "NOTAS:";
            ws.Cells[row, 1].Style.Font.Bold = true;
            row++;
            ws.Cells[row, 1, row, lastCol].Merge = true;
            ws.Cells[row, 1].Value = "Generado automáticamente por PIH — Plataforma Integral Hidrometeorológica";
            ws.Cells[row, 1].Style.Font.Italic = true;
            ws.Cells[row, 1].Style.Font.Size = 8;
            ws.Cells[row, 1].Style.Font.Color.SetColor(System.Drawing.Color.Gray);
            
            // ── Column widths ──
            ws.Column(1).Width = 5;
            ws.Column(2).Width = 26;
            for (int i = hourStartCol; i <= 26; i++) ws.Column(i).Width = 7;
            ws.Column(totalCol).Width = 9;
            
            // Freeze: row after headers, column after station name
            ws.View.FreezePanes(headerRow + 1, 3);
            
            // Print landscape
            ws.PrinterSettings.Orientation = eOrientation.Landscape;
            ws.PrinterSettings.FitToPage = true;
            ws.PrinterSettings.FitToWidth = 1;
            ws.PrinterSettings.FitToHeight = 0;
        }
        
        // ============================================================
        //  REPORTE LLUVIA – Resumen por cuenca (dinámico desde BD)
        // ============================================================
        private void BuildReporteLluviaSheet(ExcelPackage package, HourlyReportResponse report, DateTime startTime)
        {
            var ws = package.Workbook.Worksheets.Add("Reporte Lluvia");
            
            // Build dynamic cuenca groups (exclude Indefinida/empty)
            var cuencaNames = report.Stations
                .Select(s => s.Cuenca ?? "")
                .Where(c => !string.IsNullOrWhiteSpace(c) && !c.Equals("Indefinida", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();
            
            if (cuencaNames.Count == 0) return;
            
            var cuencaStations = new Dictionary<string, List<HourlyReportData>>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in cuencaNames)
                cuencaStations[name] = report.Stations.Where(s => string.Equals(s.Cuenca, name, StringComparison.OrdinalIgnoreCase)).ToList();
            
            int numCuencas = cuencaNames.Count;
            int lastDataCol = numCuencas + 1;
            int row = 1;
            
            // Title
            ws.Cells[row, 1, row, lastDataCol].Merge = true;
            ws.Cells[row, 1].Value = "REPORTE DE PRECIPITACIÓN HORARIA (mm)";
            ws.Cells[row, 1].Style.Font.Bold = true;
            ws.Cells[row, 1].Style.Font.Size = 12;
            ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            ws.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(27, 94, 32));
            ws.Cells[row, 1].Style.Font.Color.SetColor(System.Drawing.Color.White);
            row += 2;
            
            // Headers
            ws.Cells[row, 1].Value = "Hora";
            for (int g = 0; g < numCuencas; g++)
                ws.Cells[row, g + 2].Value = cuencaNames[g];
            
            using (var range = ws.Cells[row, 1, row, lastDataCol])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(76, 175, 80));
                range.Style.Font.Color.SetColor(System.Drawing.Color.White);
                range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
            }
            row++;
            
            // Precompute value maps per station
            var stationMaps = report.Stations.ToDictionary(
                s => s.StationId,
                s => BuildValueMap(s));
            
            double[] groupTotals = new double[numCuencas];
            
            for (int i = 1; i <= 24; i++)
            {
                var ht = startTime.AddHours(i);
                int h = ht.Hour == 0 ? 24 : ht.Hour;
                ws.Cells[row, 1].Value = h;
                ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                
                for (int g = 0; g < numCuencas; g++)
                {
                    var stationsInGroup = cuencaStations[cuencaNames[g]];
                    double sum = 0; int cnt = 0;
                    foreach (var station in stationsInGroup)
                    {
                        if (station.EnMantenimiento) continue;
                        if (stationMaps.TryGetValue(station.StationId, out var vmap) &&
                            vmap.TryGetValue(DtKey(ht), out var hv) && hv.Value.HasValue && hv.IsValid)
                        {
                            sum += (double)hv.Value.Value;
                            cnt++;
                        }
                    }
                    double avg = cnt > 0 ? sum / cnt : 0;
                    ws.Cells[row, g + 2].Value = avg;
                    ws.Cells[row, g + 2].Style.Numberformat.Format = "0.0";
                    ws.Cells[row, g + 2].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    if (avg > 0) ApplyPrecipColor(ws.Cells[row, g + 2], avg);
                    groupTotals[g] += avg;
                }
                
                // Alternate row shading
                if (i % 2 == 0)
                {
                    using var range = ws.Cells[row, 1, row, lastDataCol];
                    range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(245, 245, 245));
                }
                row++;
            }
            
            // Sum row
            ws.Cells[row, 1].Value = "Suma";
            ws.Cells[row, 1].Style.Font.Bold = true;
            for (int g = 0; g < numCuencas; g++)
            {
                ws.Cells[row, g + 2].Value = groupTotals[g];
                ws.Cells[row, g + 2].Style.Numberformat.Format = "0.0";
                ws.Cells[row, g + 2].Style.Font.Bold = true;
            }
            using (var range = ws.Cells[row, 1, row, lastDataCol])
            {
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(255, 248, 225));
                range.Style.Border.Top.Style = ExcelBorderStyle.Medium;
            }
            
            ws.Column(1).Width = 8;
            for (int i = 2; i <= lastDataCol; i++) ws.Column(i).Width = 18;
            ws.View.FreezePanes(4, 2);
        }
        
        // ============================================================
        //  GENÉRICO – Reporte para otras variables (nivel, temp, etc.)
        // ============================================================
        private static string GetVariableUnits(string variable)
        {
            var v = variable.ToLower();
            if (v.Contains("precipitación")) return "mm";
            if (v.Contains("nivel")) return "m";
            if (v.Contains("temperatura")) return "°C";
            if (v.Contains("velocidad") && v.Contains("viento")) return "m/s";
            if (v.Contains("dirección") && v.Contains("viento")) return "°";
            if (v.Contains("humedad")) return "%";
            if (v.Contains("presión") || v.Contains("presion")) return "hPa";
            if (v.Contains("radiación") || v.Contains("radiacion")) return "W/m²";
            if (v.Contains("batería") || v.Contains("bateria")) return "V";
            return "";
        }

        private static string GetVariableDisplayName(string variable)
        {
            var v = variable.ToLower();
            if (v.Contains("nivel")) return "NIVEL DE AGUA";
            if (v.Contains("temperatura")) return "TEMPERATURA";
            if (v.Contains("velocidad") && v.Contains("viento")) return "VELOCIDAD DEL VIENTO";
            if (v.Contains("dirección") && v.Contains("viento")) return "DIRECCIÓN DEL VIENTO";
            if (v.Contains("humedad")) return "HUMEDAD RELATIVA";
            if (v.Contains("presión") || v.Contains("presion")) return "PRESIÓN BAROMÉTRICA";
            if (v.Contains("radiación") || v.Contains("radiacion")) return "RADIACIÓN SOLAR";
            if (v.Contains("batería") || v.Contains("bateria")) return "BATERÍA";
            return variable.ToUpper().Replace("_", " ");
        }

        /// <summary>
        /// Determina si la variable es acumulativa (se suma) o instantánea (se promedia).
        /// </summary>
        private static bool IsAccumulativeVariable(string variable)
        {
            var v = variable.ToLower();
            return v.Contains("precipitación");
        }

        private void BuildGenericSheet(ExcelPackage package, HourlyReportResponse report, string variable, DateTime startTime, bool grouped)
        {
            var endTime = DateTime.Parse(report.EndTime);
            string displayName = GetVariableDisplayName(variable);
            string units = GetVariableUnits(variable);
            bool isAccumulative = IsAccumulativeVariable(variable);
            string aggregateLabel = isAccumulative ? "TOTAL" : "PROMEDIO";
            string fmt = isAccumulative ? "0.0" : "0.00";

            // Sheet name (max 31 chars, no invalid chars)
            string sheetName = displayName.Length > 31 ? displayName.Substring(0, 31) : displayName;
            var ws = package.Workbook.Worksheets.Add(sheetName);

            // Colors (same as precipitation sheet)
            var darkGreen  = System.Drawing.Color.FromArgb(27, 94, 32);
            var medGreen   = System.Drawing.Color.FromArgb(46, 125, 50);
            var lightGreen = System.Drawing.Color.FromArgb(76, 175, 80);
            var paleGreen  = System.Drawing.Color.FromArgb(232, 245, 233);
            var gold       = System.Drawing.Color.FromArgb(194, 147, 28);
            var white      = System.Drawing.Color.White;

            const int hourStartCol = 3;
            const int totalCol = 27;
            const int lastCol = 27;

            int row = 1;

            // ── Row 1: Title ──
            ws.Cells[row, 1, row, lastCol].Merge = true;
            SetCell(ws, row, 1, "DEPARTAMENTO REGIONAL DE HIDROMETRÍA", "Arial", 16, true, white, darkGreen, ExcelHorizontalAlignment.Center);
            ws.Row(row).Height = 28;
            row++;

            // ── Row 2: Subtitle ──
            ws.Cells[row, 1, row, lastCol].Merge = true;
            SetCell(ws, row, 1, "PUESTO CENTRAL DE REGISTRO", "Arial", 12, true, white, darkGreen, ExcelHorizontalAlignment.Center);
            row++;

            // ── Row 3: Description ──
            ws.Cells[row, 1, row, lastCol].Merge = true;
            SetCell(ws, row, 1, "Reporte de la Información Hidrometeorológica Automática", "Arial", 12, true, white, medGreen, ExcelHorizontalAlignment.Center);
            row++;

            // ── Row 4: Variable + Date ──
            ws.Cells[row, 1, row, 20].Merge = true;
            SetCell(ws, row, 1, $"REPORTE DE {displayName}", "Arial", 12, true, white, medGreen, ExcelHorizontalAlignment.Center);
            ws.Cells[row, 21, row, lastCol].Merge = true;
            string unitsLabel = string.IsNullOrEmpty(units) ? variable : $"{variable} ({units})";
            SetCell(ws, row, 21, $"FECHA: {endTime:dd/MM/yyyy}     {unitsLabel}", "Arial", 10, true, white, medGreen, ExcelHorizontalAlignment.Center);
            row++;

            // ── Row 5: Column headers ──
            ws.Cells[row, 1].Value = "No.";
            ws.Cells[row, 2].Value = "ESTACIÓN";
            for (int i = 1; i <= 24; i++)
            {
                var ht = startTime.AddHours(i);
                int h = ht.Hour == 0 ? 24 : ht.Hour;
                ws.Cells[row, i + 2].Value = h;
            }
            ws.Cells[row, totalCol].Value = aggregateLabel;
            using (var range = ws.Cells[row, 1, row, lastCol])
            {
                range.Style.Font.Bold = true;
                range.Style.Font.Name = "Arial";
                range.Style.Font.Size = 10;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(lightGreen);
                range.Style.Font.Color.SetColor(white);
                range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
                range.Style.Border.Bottom.Color.SetColor(darkGreen);
            }
            ws.Cells[row, totalCol].Style.Fill.BackgroundColor.SetColor(gold);
            ws.Cells[row, totalCol].Style.Font.Color.SetColor(System.Drawing.Color.Black);
            int headerRow = row;
            row++;

            // ── Data grouped by cuenca / subcuenca (siempre agrupado) ──
            var cuencaGroups = report.Stations
                .GroupBy(s => string.IsNullOrWhiteSpace(s.Cuenca) || s.Cuenca.Equals("Indefinida", StringComparison.OrdinalIgnoreCase)
                    ? "OTRAS ESTACIONES" : s.Cuenca)
                .OrderBy(g => g.Key == "OTRAS ESTACIONES" ? 1 : 0)
                .ThenBy(g => g.Key);
            int stationNum = 1;

            foreach (var cuencaGroup in cuencaGroups)
            {
                string cuencaLabel = cuencaGroup.Key;

                var subGroups = cuencaGroup
                    .GroupBy(s => string.IsNullOrWhiteSpace(s.Subcuenca) || s.Subcuenca.Equals("Indefinida", StringComparison.OrdinalIgnoreCase)
                        ? "" : s.Subcuenca)
                    .OrderBy(g => g.Key);

                foreach (var subGroup in subGroups)
                {
                    // Header row
                    string header = string.IsNullOrEmpty(subGroup.Key)
                        ? $"CUENCA {cuencaLabel.ToUpper()}"
                        : $"SUBCUENCA {subGroup.Key.ToUpper()} — {cuencaLabel.ToUpper()}";

                    ws.Cells[row, 1, row, lastCol].Merge = true;
                    SetCell(ws, row, 1, header, "Arial", 10, true,
                        darkGreen, System.Drawing.Color.FromArgb(200, 230, 201), ExcelHorizontalAlignment.Left);
                    row++;

                    var stations = subGroup.OrderBy(s => s.StationName).ToList();

                    double[] hourSums = new double[24];
                    int[] hourCounts = new int[24];

                    foreach (var station in stations)
                    {
                        bool isMaint = station.EnMantenimiento;
                        ws.Cells[row, 1].Value = stationNum++;
                        ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        ws.Cells[row, 2].Value = isMaint ? $"{station.StationName} [MTTO]" : station.StationName;
                        ws.Cells[row, 2].Style.Font.Size = 9;
                        if (isMaint)
                        {
                            ws.Cells[row, 2].Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(249, 115, 22));
                            ws.Cells[row, 2].Style.Font.Italic = true;
                        }

                        var vmap = BuildValueMap(station);
                        double rowSum = 0;
                        int rowCount = 0;
                        bool hasAny = false;

                        for (int i = 1; i <= 24; i++)
                        {
                            var ht = startTime.AddHours(i);
                            int col = i + 2;

                            if (vmap.TryGetValue(DtKey(ht), out var hv) && hv.Value.HasValue)
                            {
                                double val = (double)hv.Value.Value;
                                ws.Cells[row, col].Value = val;
                                ws.Cells[row, col].Style.Numberformat.Format = fmt;
                                ws.Cells[row, col].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                                if (hv.IsValid && !isMaint)
                                {
                                    rowSum += val;
                                    rowCount++;
                                    hourSums[i - 1] += val;
                                    hourCounts[i - 1]++;
                                }
                                else
                                {
                                    ws.Cells[row, col].Style.Font.Color.SetColor(System.Drawing.Color.Red);
                                    ws.Cells[row, col].Style.Font.Strike = true;
                                }
                                hasAny = true;
                            }
                            else
                            {
                                ws.Cells[row, col].Value = "-";
                                ws.Cells[row, col].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                                ws.Cells[row, col].Style.Font.Color.SetColor(System.Drawing.Color.Gray);
                            }
                        }

                        // Aggregate column (TOTAL for accumulative, PROMEDIO for instantaneous)
                        if (hasAny && rowCount > 0)
                        {
                            double aggregate = isAccumulative ? rowSum : rowSum / rowCount;
                            ws.Cells[row, totalCol].Value = aggregate;
                            ws.Cells[row, totalCol].Style.Numberformat.Format = fmt;
                            ws.Cells[row, totalCol].Style.Font.Bold = true;
                            ws.Cells[row, totalCol].Style.Fill.PatternType = ExcelFillStyle.Solid;
                            ws.Cells[row, totalCol].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(255, 248, 225));
                        }
                        else
                        {
                            ws.Cells[row, totalCol].Value = "-";
                            ws.Cells[row, totalCol].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        }

                        // Thin bottom border
                        using (var range = ws.Cells[row, 1, row, lastCol])
                        {
                            range.Style.Border.Bottom.Style = ExcelBorderStyle.Hair;
                            range.Style.Border.Bottom.Color.SetColor(System.Drawing.Color.FromArgb(200, 200, 200));
                        }
                        row++;
                    }

                    // ── Average row ──
                    string avgLabel = isAccumulative
                        ? "Precipitación media horaria"
                        : $"Promedio horario — {displayName.ToLower()}";
                    ws.Cells[row, 2].Value = avgLabel;
                    ws.Cells[row, 2].Style.Font.Italic = true;
                    ws.Cells[row, 2].Style.Font.Size = 9;
                    using (var range = ws.Cells[row, 1, row, lastCol])
                    {
                        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        range.Style.Fill.BackgroundColor.SetColor(paleGreen);
                        range.Style.Font.Bold = true;
                        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Top.Color.SetColor(medGreen);
                        range.Style.Border.Bottom.Color.SetColor(medGreen);
                    }
                    double avgSum = 0;
                    int avgCount = 0;
                    for (int i = 0; i < 24; i++)
                    {
                        int col = i + hourStartCol;
                        if (hourCounts[i] > 0)
                        {
                            double avg = hourSums[i] / hourCounts[i];
                            ws.Cells[row, col].Value = avg;
                            ws.Cells[row, col].Style.Numberformat.Format = fmt;
                            ws.Cells[row, col].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                            avgSum += avg;
                            avgCount++;
                        }
                        else
                        {
                            ws.Cells[row, col].Value = "-";
                            ws.Cells[row, col].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        }
                    }
                    double finalAvg = isAccumulative ? avgSum : (avgCount > 0 ? avgSum / avgCount : 0);
                    ws.Cells[row, totalCol].Value = finalAvg;
                    ws.Cells[row, totalCol].Style.Numberformat.Format = fmt;
                    ws.Cells[row, totalCol].Style.Font.Bold = true;
                    row += 2; // blank separator
                }
            }

            // ── Notes ──
            ws.Cells[row, 1].Value = "NOTAS:";
            ws.Cells[row, 1].Style.Font.Bold = true;
            row++;
            ws.Cells[row, 1, row, lastCol].Merge = true;
            ws.Cells[row, 1].Value = "Generado automáticamente por PIH — Plataforma Integral Hidrometeorológica";
            ws.Cells[row, 1].Style.Font.Italic = true;
            ws.Cells[row, 1].Style.Font.Size = 8;
            ws.Cells[row, 1].Style.Font.Color.SetColor(System.Drawing.Color.Gray);

            // ── Column widths ──
            ws.Column(1).Width = 5;
            ws.Column(2).Width = 26;
            for (int i = hourStartCol; i <= 26; i++) ws.Column(i).Width = 7;
            ws.Column(totalCol).Width = 9;

            // Freeze: row after headers, column after station name
            ws.View.FreezePanes(headerRow + 1, 3);

            // Print landscape
            ws.PrinterSettings.Orientation = eOrientation.Landscape;
            ws.PrinterSettings.FitToPage = true;
            ws.PrinterSettings.FitToWidth = 1;
            ws.PrinterSettings.FitToHeight = 0;
        }
        
        // ============================================================
        //  Helpers
        // ============================================================
        private static void SetCell(ExcelWorksheet ws, int row, int col, string value,
            string fontName, int fontSize, bool bold,
            System.Drawing.Color fontColor, System.Drawing.Color bgColor,
            ExcelHorizontalAlignment align)
        {
            ws.Cells[row, col].Value = value;
            ws.Cells[row, col].Style.Font.Name = fontName;
            ws.Cells[row, col].Style.Font.Size = fontSize;
            ws.Cells[row, col].Style.Font.Bold = bold;
            ws.Cells[row, col].Style.Font.Color.SetColor(fontColor);
            ws.Cells[row, col].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[row, col].Style.Fill.BackgroundColor.SetColor(bgColor);
            ws.Cells[row, col].Style.HorizontalAlignment = align;
        }
        
        private static void ApplyPrecipColor(ExcelRange cell, double value)
        {
            if (value <= 0) return;
            var color = value switch
            {
                >= 50 => System.Drawing.Color.FromArgb(127, 0, 255),
                >= 25 => System.Drawing.Color.FromArgb(255, 0, 0),
                >= 10 => System.Drawing.Color.FromArgb(255, 127, 0),
                >= 5  => System.Drawing.Color.FromArgb(255, 255, 0),
                >= 2.5 => System.Drawing.Color.FromArgb(144, 238, 144),
                _ => System.Drawing.Color.FromArgb(173, 216, 230)
            };
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(color);
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
