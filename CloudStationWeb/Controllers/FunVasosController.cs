using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using CloudStationWeb.Services;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace CloudStationWeb.Controllers
{
    public class FunVasosReferenciaDto
    {
        public long Id { get; set; }
        public string PresaKey { get; set; } = "";
        public string Nombre { get; set; } = "";
        public decimal Valor { get; set; }
        public string Color { get; set; } = "#ffff00";
        public bool Visible { get; set; } = true;
    }

    [Authorize(AuthenticationSchemes = "Identity.Application," + JwtBearerDefaults.AuthenticationScheme)]
    public class FunVasosController : Controller
    {
        private readonly FunVasosService _service;
        private readonly string _sqlConn;
        private readonly ILogger<FunVasosController> _logger;

        public FunVasosController(FunVasosService service, IConfiguration config, ILogger<FunVasosController> logger)
        {
            _service = service;
            _sqlConn = config.GetConnectionString("SqlServer")!;
            _logger = logger;
        }

        private async Task EnsureReferenciaTableAsync()
        {
            using var db = new SqlConnection(_sqlConn);
            await db.ExecuteAsync(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FunVasosReferencias')
                BEGIN
                    CREATE TABLE FunVasosReferencias (
                        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
                        PresaKey NVARCHAR(100) NOT NULL,
                        Nombre NVARCHAR(200) NOT NULL,
                        Valor DECIMAL(18,4) NOT NULL,
                        Color NVARCHAR(20) DEFAULT '#ffff00',
                        Visible BIT DEFAULT 1,
                        UsuarioModifica NVARCHAR(256),
                        FechaCreacion DATETIME2 DEFAULT GETDATE(),
                        FechaModifica DATETIME2 DEFAULT GETDATE()
                    );
                    CREATE INDEX IX_FunVasosReferencias_PresaKey ON FunVasosReferencias(PresaKey);
                END");
        }

        // GET: /FunVasos
        public async Task<IActionResult> Index(DateTime? fechaInicio, DateTime? fechaFin)
        {
            var model = await _service.GetDataAsync(fechaInicio, fechaFin);
            return View(model);
        }

        // API: /FunVasos/GetData?fechaInicio=2026-03-24&fechaFin=2026-03-26
        [HttpGet]
        public async Task<IActionResult> GetData(DateTime? fechaInicio, DateTime? fechaFin)
        {
            var model = await _service.GetDataAsync(fechaInicio, fechaFin);
            return Json(model);
        }

        // API ligero: /FunVasos/GetCascadeData — último dato de cada presa
        [HttpGet]
        public async Task<IActionResult> GetCascadeData()
        {
            var model = await _service.GetDataAsync(null, null); // latest date
            var result = model.Presas.Select(p =>
            {
                var last = p.Datos.LastOrDefault();
                return new
                {
                    key = p.Presa.Normalize(System.Text.NormalizationForm.FormD)
                        .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                            != System.Globalization.UnicodeCategory.NonSpacingMark)
                        .Aggregate("", (s, c) => s + c)
                        .Replace(' ', '_'),
                    name = p.Presa,
                    currentElev = p.UltimaElevacion,
                    generation = p.TotalGeneracion,
                    activeUnits = (int)(last?.NumUnidades ?? 0),
                    almacenamiento = p.UltimoAlmacenamiento,
                    ultimaHora = p.UltimaHora,
                    fecha = model.FechaFin.ToString("yyyy-MM-dd"),
                    aportacionesV = last?.AportacionesV,
                    extraccionesV = last?.ExtraccionesTotalV
                };
            }).ToList();
            return Json(new { presas = result, fecha = model.FechaFin.ToString("yyyy-MM-dd") });
        }

        // GET: /FunVasos/GetReferencias?presaKey=Angostura
        [HttpGet]
        public async Task<IActionResult> GetReferencias(string? presaKey)
        {
            _logger.LogWarning("[FunVasos] GetReferencias called. presaKey='{PresaKey}'", presaKey);
            try
            {
                await EnsureReferenciaTableAsync();
                using var db = new SqlConnection(_sqlConn);
                string sql = string.IsNullOrEmpty(presaKey)
                    ? "SELECT Id, PresaKey, Nombre, Valor, Color, Visible FROM FunVasosReferencias ORDER BY PresaKey, Nombre"
                    : "SELECT Id, PresaKey, Nombre, Valor, Color, Visible FROM FunVasosReferencias WHERE PresaKey = @PresaKey ORDER BY Nombre";
                var refs = await db.QueryAsync<FunVasosReferenciaDto>(sql, new { PresaKey = presaKey });
                return Json(new { success = true, data = refs });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /FunVasos/SaveReferencia
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveReferencia([FromBody] FunVasosReferenciaDto model)
        {
            _logger.LogInformation("[FunVasos] SaveReferencia called. Model is null: {IsNull}", model == null);
            if (model != null)
                _logger.LogInformation("[FunVasos] SaveReferencia data: Id={Id}, PresaKey={PresaKey}, Nombre={Nombre}, Valor={Valor}", model.Id, model.PresaKey, model.Nombre, model.Valor);

            if (model == null || string.IsNullOrWhiteSpace(model.PresaKey) || string.IsNullOrWhiteSpace(model.Nombre))
                return Json(new { success = false, message = "Datos inválidos: PresaKey='" + model?.PresaKey + "' Nombre='" + model?.Nombre + "'" });

            try
            {
                await EnsureReferenciaTableAsync();
                using var db = new SqlConnection(_sqlConn);
                if (model.Id > 0)
                {
                    await db.ExecuteAsync(@"
                        UPDATE FunVasosReferencias 
                        SET Nombre = @Nombre, Valor = @Valor, Color = @Color, Visible = @Visible,
                            UsuarioModifica = @User, FechaModifica = GETDATE()
                        WHERE Id = @Id",
                        new { model.Id, model.Nombre, model.Valor, model.Color, model.Visible, User = User.Identity?.Name ?? "Admin" });
                }
                else
                {
                    model.Id = await db.ExecuteScalarAsync<long>(@"
                        INSERT INTO FunVasosReferencias (PresaKey, Nombre, Valor, Color, Visible, UsuarioModifica)
                        OUTPUT INSERTED.Id
                        VALUES (@PresaKey, @Nombre, @Valor, @Color, @Visible, @User)",
                        new { model.PresaKey, model.Nombre, model.Valor, model.Color, model.Visible, User = User.Identity?.Name ?? "Admin" });
                }
                return Json(new { success = true, referencia = model });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /FunVasos/DeleteReferencia?id=1
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteReferencia(long id)
        {
            if (id <= 0) return Json(new { success = false, message = "ID inválido" });
            try
            {
                using var db = new SqlConnection(_sqlConn);
                await db.ExecuteAsync("DELETE FROM FunVasosReferencias WHERE Id = @Id", new { Id = id });
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /FunVasos/ToggleReferencia
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleReferencia([FromBody] FunVasosReferenciaDto model)
        {
            if (model == null || model.Id <= 0) return Json(new { success = false, message = "ID inválido" });
            try
            {
                using var db = new SqlConnection(_sqlConn);
                await db.ExecuteAsync("UPDATE FunVasosReferencias SET Visible = @Visible, FechaModifica = GETDATE() WHERE Id = @Id",
                    new { model.Id, model.Visible });
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: /FunVasos/ExportNivelesHorarios?fecha=2026-03-24
        [HttpGet]
        public async Task<IActionResult> ExportNivelesHorarios(string? fecha)
        {
            DateTime target = DateTime.Today;
            if (!string.IsNullOrEmpty(fecha) && DateTime.TryParse(fecha, out var parsed))
                target = parsed;

            var model = await _service.GetDataAsync(target, target);

            var presasReport = new[] { "Angostura", "Chicoasén", "Malpaso", "Peñitas" };
            var namoValues = new Dictionary<string, double> { { "Chicoasén", 392.50 }, { "Peñitas", 87.40 } };

            // Build hour→elevacion maps per presa
            var hourMaps = new Dictionary<string, Dictionary<int, double?>>();
            foreach (var pName in presasReport)
            {
                var presa = model.Presas.FirstOrDefault(p => p.Presa == pName);
                var map = new Dictionary<int, double?>();
                if (presa != null)
                {
                    foreach (var d in presa.Datos)
                        map[d.Hora] = d.Elevacion;
                }
                hourMaps[pName] = map;
            }

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Niveles Horarios");

            var darkGreen = System.Drawing.Color.FromArgb(27, 94, 32);
            var medGreen = System.Drawing.Color.FromArgb(46, 125, 50);
            var white = System.Drawing.Color.White;
            var headerBg = System.Drawing.Color.FromArgb(33, 150, 136);
            var subHeaderBg = System.Drawing.Color.FromArgb(56, 142, 60);

            int row = 1;
            // Title
            ws.Cells[row, 1, row, 7].Merge = true;
            ws.Cells[row, 1].Value = "REPORTE DE NIVELES HORARIOS";
            ws.Cells[row, 1].Style.Font.Bold = true;
            ws.Cells[row, 1].Style.Font.Size = 14;
            ws.Cells[row, 1].Style.Font.Color.SetColor(white);
            ws.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(darkGreen);
            ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            ws.Row(row).Height = 28;
            row++;

            // Date
            ws.Cells[row, 1, row, 7].Merge = true;
            ws.Cells[row, 1].Value = $"FECHA: {target:dd 'de' MMMM 'de' yyyy}";
            ws.Cells[row, 1].Style.Font.Bold = true;
            ws.Cells[row, 1].Style.Font.Size = 11;
            ws.Cells[row, 1].Style.Font.Color.SetColor(white);
            ws.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(medGreen);
            ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            row++;

            // Header row 1 (presa names with merged columns)
            void SetHeader(int r, int c, string text, System.Drawing.Color bg)
            {
                ws.Cells[r, c].Value = text;
                ws.Cells[r, c].Style.Font.Bold = true;
                ws.Cells[r, c].Style.Font.Size = 10;
                ws.Cells[r, c].Style.Font.Color.SetColor(white);
                ws.Cells[r, c].Style.Fill.PatternType = ExcelFillStyle.Solid;
                ws.Cells[r, c].Style.Fill.BackgroundColor.SetColor(bg);
                ws.Cells[r, c].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                ws.Cells[r, c].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            }

            row++;
            // Col layout: 1=Hora, 2=Angostura Nivel, 3=Chico Nivel, 4=Chico Dif, 5=Malpaso Nivel, 6=Peñitas Nivel, 7=Peñitas Dif
            ws.Cells[row, 1, row + 1, 1].Merge = true;
            SetHeader(row, 1, "Hora", headerBg);
            ws.Cells[row, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            SetHeader(row, 2, "Angostura", headerBg);

            ws.Cells[row, 3, row, 4].Merge = true;
            SetHeader(row, 3, "Chicoasén", headerBg);

            SetHeader(row, 5, "Malpaso", headerBg);

            ws.Cells[row, 6, row, 7].Merge = true;
            SetHeader(row, 6, "Peñitas", headerBg);
            row++;

            // Sub-header row
            SetHeader(row, 2, "Nivel (msnm)", subHeaderBg);
            SetHeader(row, 3, "Nivel (msnm)", subHeaderBg);
            SetHeader(row, 4, "Dif. Al NAMO", subHeaderBg);
            SetHeader(row, 5, "Nivel (msnm)", subHeaderBg);
            SetHeader(row, 6, "Nivel (msnm)", subHeaderBg);
            SetHeader(row, 7, "Dif. Al NAMO", subHeaderBg);
            row++;

            // Data rows (hours 1 to 24)
            int dataStartRow = row;
            for (int h = 1; h <= 24; h++)
            {
                ws.Cells[row, 1].Value = h;
                ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                ws.Cells[row, 1].Style.Font.Bold = true;

                var aVal = hourMaps["Angostura"].GetValueOrDefault(h);
                var cVal = hourMaps["Chicoasén"].GetValueOrDefault(h);
                var mVal = hourMaps["Malpaso"].GetValueOrDefault(h);
                var pVal = hourMaps["Peñitas"].GetValueOrDefault(h);

                if (aVal.HasValue) { ws.Cells[row, 2].Value = aVal.Value; ws.Cells[row, 2].Style.Numberformat.Format = "0.00"; }
                if (cVal.HasValue) { ws.Cells[row, 3].Value = cVal.Value; ws.Cells[row, 3].Style.Numberformat.Format = "0.00"; }
                if (cVal.HasValue) { ws.Cells[row, 4].Value = namoValues["Chicoasén"] - cVal.Value; ws.Cells[row, 4].Style.Numberformat.Format = "0.00"; }
                if (mVal.HasValue) { ws.Cells[row, 5].Value = mVal.Value; ws.Cells[row, 5].Style.Numberformat.Format = "0.00"; }
                if (pVal.HasValue) { ws.Cells[row, 6].Value = pVal.Value; ws.Cells[row, 6].Style.Numberformat.Format = "0.00"; }
                if (pVal.HasValue) { ws.Cells[row, 7].Value = pVal.Value; ws.Cells[row, 7].Style.Numberformat.Format = "0.00"; ws.Cells[row, 7].Value = namoValues["Peñitas"] - pVal.Value; }

                for (int c = 1; c <= 7; c++)
                    ws.Cells[row, c].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                // Alternate row color
                if (h % 2 == 0)
                {
                    using var rng = ws.Cells[row, 1, row, 7];
                    rng.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    rng.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(232, 245, 233));
                }
                row++;
            }

            // Column widths
            ws.Column(1).Width = 8;
            for (int c = 2; c <= 7; c++) ws.Column(c).Width = 16;

            // Borders for the entire data area
            using (var rng = ws.Cells[dataStartRow - 2, 1, dataStartRow + 23, 7])
            {
                rng.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                rng.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                rng.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                rng.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            }

            var stream = new MemoryStream();
            package.SaveAs(stream);
            stream.Position = 0;

            var fileName = $"NivelesHorarios_{target:yyyyMMdd}.xlsx";
            return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
    }
}
