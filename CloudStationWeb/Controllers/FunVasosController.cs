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

    public class EmbalseConfigDto
    {
        public long Id { get; set; }
        public string PresaKey { get; set; } = "";
        public string NombreDisplay { get; set; } = "";
        public decimal? Namo { get; set; }
        public decimal? Name { get; set; }
        public decimal? Namino { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        // Extended fields for full dynamic support
        public string Color { get; set; } = "#00e676";
        public string? HydroKey { get; set; }
        public string? CuencaCode { get; set; }
        public int? ExcelHeaderRow { get; set; }
        public int? ExcelDataStartRow { get; set; }
        public int? ExcelDataEndRow { get; set; }
        public bool IsTaponType { get; set; }
        public int TotalUnits { get; set; }
        public string? BhgKey { get; set; }
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

            // Load EmbalseConfig for dynamic rendering in the view
            try
            {
                using var db = new SqlConnection(_sqlConn);
                var configs = (await db.QueryAsync<EmbalseConfigDto>(
                    "SELECT Id, PresaKey, NombreDisplay, NAMO, NAME, NAMINO, IsActive, SortOrder, Color, HydroKey, CuencaCode, ExcelHeaderRow, ExcelDataStartRow, ExcelDataEndRow, IsTaponType, TotalUnits, BhgKey FROM EmbalseConfig WHERE IsActive = 1 ORDER BY SortOrder")).ToList();
                ViewBag.EmbalseConfigs = configs;
            }
            catch { ViewBag.EmbalseConfigs = new List<EmbalseConfigDto>(); }

            return View(model);
        }

        // API: /FunVasos/GetData?fechaInicio=2026-03-24&fechaFin=2026-03-26
        [HttpGet]
        public async Task<IActionResult> GetData(DateTime? fechaInicio, DateTime? fechaFin)
        {
            var model = await _service.GetDataAsync(fechaInicio, fechaFin);
            return Json(model);
        }

        // API ligero: /FunVasos/GetCascadeData — último dato de cada presa (incluye config embalse)
        [HttpGet]
        public async Task<IActionResult> GetCascadeData()
        {
            var model = await _service.GetDataAsync(null, null); // latest date

            // Load EmbalseConfig for cascade enrichment
            Dictionary<string, EmbalseConfigDto> configByDisplay;
            try
            {
                using var db = new SqlConnection(_sqlConn);
                var configs = await db.QueryAsync<EmbalseConfigDto>(
                    "SELECT Id, PresaKey, NombreDisplay, NAMO, NAME, NAMINO, IsActive, SortOrder, Color, HydroKey, CuencaCode, ExcelHeaderRow, ExcelDataStartRow, ExcelDataEndRow, IsTaponType, TotalUnits, BhgKey FROM EmbalseConfig WHERE IsActive = 1 ORDER BY SortOrder");
                configByDisplay = configs.ToDictionary(c => c.NombreDisplay, c => c);
            }
            catch { configByDisplay = new Dictionary<string, EmbalseConfigDto>(); }

            var result = model.Presas.Select(p =>
            {
                var last = p.Datos.LastOrDefault();
                configByDisplay.TryGetValue(p.Presa, out var cfg);
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
                    extraccionesV = last?.ExtraccionesTotalV,
                    namo = cfg?.Namo,
                    namino = cfg?.Namino,
                    totalUnits = cfg?.TotalUnits ?? 0,
                    isTapon = cfg?.IsTaponType ?? false,
                    color = cfg?.Color
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

            // Load active embalses from config
            await EnsureEmbalseConfigTableAsync();
            var embalseConfigs = new List<EmbalseConfigDto>();
            using (var db = new SqlConnection(_sqlConn))
            {
                embalseConfigs = (await db.QueryAsync<EmbalseConfigDto>(
                    "SELECT Id, PresaKey, NombreDisplay, Namo, Name, Namino, IsActive, SortOrder, Color FROM EmbalseConfig WHERE IsActive = 1 ORDER BY SortOrder")).ToList();
            }

            var presasReport = embalseConfigs.Select(c => c.NombreDisplay).ToArray();
            var namoValues = embalseConfigs.Where(c => c.Namo.HasValue).ToDictionary(c => c.NombreDisplay, c => (double)c.Namo!.Value);

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

            // Header helper
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

            // Dynamic column layout: Col 1 = Hora, then 1-2 cols per presa (Nivel, optionally Dif al NAMO)
            ws.Cells[row, 1, row + 1, 1].Merge = true;
            SetHeader(row, 1, "Hora", headerBg);
            ws.Cells[row, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            int col = 2;
            var presaColMap = new List<(string nombre, int nivelCol, int? difCol)>();
            foreach (var presa in presasReport)
            {
                bool hasDif = namoValues.ContainsKey(presa);
                int startCol = col;
                if (hasDif)
                {
                    ws.Cells[row, col, row, col + 1].Merge = true;
                    SetHeader(row, col, presa, headerBg);
                    presaColMap.Add((presa, col, col + 1));
                    col += 2;
                }
                else
                {
                    SetHeader(row, col, presa, headerBg);
                    presaColMap.Add((presa, col, null));
                    col++;
                }
            }
            int totalCols = col - 1;
            row++;

            // Sub-header row
            foreach (var pm in presaColMap)
            {
                SetHeader(row, pm.nivelCol, "Nivel (msnm)", subHeaderBg);
                if (pm.difCol.HasValue)
                    SetHeader(row, pm.difCol.Value, "Dif. Al NAMO", subHeaderBg);
            }
            row++;

            // Data rows (hours 1 to 24)
            int dataStartRow = row;
            for (int h = 1; h <= 24; h++)
            {
                ws.Cells[row, 1].Value = h;
                ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                ws.Cells[row, 1].Style.Font.Bold = true;

                foreach (var pm in presaColMap)
                {
                    var val = hourMaps[pm.nombre].GetValueOrDefault(h);
                    if (val.HasValue)
                    {
                        ws.Cells[row, pm.nivelCol].Value = val.Value;
                        ws.Cells[row, pm.nivelCol].Style.Numberformat.Format = "0.00";
                    }
                    if (pm.difCol.HasValue && val.HasValue && namoValues.TryGetValue(pm.nombre, out var namoVal))
                    {
                        ws.Cells[row, pm.difCol.Value].Value = namoVal - val.Value;
                        ws.Cells[row, pm.difCol.Value].Style.Numberformat.Format = "0.00";
                    }
                }

                for (int c = 1; c <= totalCols; c++)
                    ws.Cells[row, c].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                // Alternate row color
                if (h % 2 == 0)
                {
                    using var rng = ws.Cells[row, 1, row, totalCols];
                    rng.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    rng.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(232, 245, 233));
                }
                row++;
            }

            // Column widths
            ws.Column(1).Width = 8;
            for (int c = 2; c <= totalCols; c++) ws.Column(c).Width = 16;

            // Borders for the entire data area
            using (var rng = ws.Cells[dataStartRow - 2, 1, dataStartRow + 23, totalCols])
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

        // =====================================================================
        // EMBALSE CONFIG — NAMO / NAME / NAMINO configurables por presa
        // =====================================================================

        private async Task EnsureEmbalseConfigTableAsync()
        {
            using var db = new SqlConnection(_sqlConn);
            await db.ExecuteAsync(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'EmbalseConfig')
                BEGIN
                    CREATE TABLE EmbalseConfig (
                        Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
                        PresaKey        NVARCHAR(100) NOT NULL,
                        NombreDisplay   NVARCHAR(200) NOT NULL,
                        Namo            DECIMAL(18,4) NULL,
                        Name            DECIMAL(18,4) NULL,
                        Namino          DECIMAL(18,4) NULL,
                        IsActive        BIT NOT NULL DEFAULT 1,
                        SortOrder       INT NOT NULL DEFAULT 0,
                        Color           NVARCHAR(20) DEFAULT '#00e676',
                        HydroKey        NVARCHAR(100) NULL,
                        CuencaCode      NVARCHAR(20) NULL,
                        ExcelHeaderRow  INT NULL,
                        ExcelDataStartRow INT NULL,
                        ExcelDataEndRow INT NULL,
                        IsTaponType     BIT NOT NULL DEFAULT 0,
                        TotalUnits      INT NOT NULL DEFAULT 0,
                        BhgKey          NVARCHAR(100) NULL,
                        UsuarioModifica NVARCHAR(256),
                        FechaCreacion   DATETIME2 DEFAULT GETDATE(),
                        FechaModifica   DATETIME2 DEFAULT GETDATE()
                    );
                    CREATE UNIQUE INDEX IX_EmbalseConfig_PresaKey ON EmbalseConfig(PresaKey);

                    INSERT INTO EmbalseConfig (PresaKey, NombreDisplay, Namo, Name, Namino, IsActive, SortOrder, Color, HydroKey, CuencaCode, ExcelHeaderRow, ExcelDataStartRow, ExcelDataEndRow, IsTaponType, TotalUnits, BhgKey)
                    VALUES
                        ('Angostura',           'Angostura',            539.00, 542.10, 510.40, 1, 1, '#1565C0', 'Angostura', 'ang', 12, 15, 38, 0, 5, 'ANGOSTURA'),
                        ('Chicoasen',           'Chicoasén',            395.00, 400.00, 378.50, 1, 2, '#42a5f5', 'Chicoasen', 'mmt', 45, 48, 70, 0, 8, 'CHICOASEN'),
                        ('Malpaso',             'Malpaso',              189.70, 192.00, 163.00, 1, 3, '#00838F', 'Malpaso',   'mps', 78, 81, 103, 0, 6, 'MALPASO'),
                        ('Tapon_Juan_Grijalva', 'Tapón Juan Grijalva',  100.00, 105.50,  87.00, 0, 4, '#ff7043', 'JGrijalva', NULL,  111, 114, 136, 1, 0, 'CANAL_JG'),
                        ('Penitas',             'Peñitas',               95.10,  99.20,  84.50, 1, 5, '#AD1457', 'Penitas',   'pea', 144, 147, 170, 0, 4, 'PEÑITAS');
                END

                -- Migration: add columns if they don't exist (for existing deployments)
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('EmbalseConfig') AND name = 'Color')
                    ALTER TABLE EmbalseConfig ADD Color NVARCHAR(20) DEFAULT '#00e676';
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('EmbalseConfig') AND name = 'HydroKey')
                    ALTER TABLE EmbalseConfig ADD HydroKey NVARCHAR(100) NULL;
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('EmbalseConfig') AND name = 'CuencaCode')
                    ALTER TABLE EmbalseConfig ADD CuencaCode NVARCHAR(20) NULL;
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('EmbalseConfig') AND name = 'ExcelHeaderRow')
                    ALTER TABLE EmbalseConfig ADD ExcelHeaderRow INT NULL;
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('EmbalseConfig') AND name = 'ExcelDataStartRow')
                    ALTER TABLE EmbalseConfig ADD ExcelDataStartRow INT NULL;
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('EmbalseConfig') AND name = 'ExcelDataEndRow')
                    ALTER TABLE EmbalseConfig ADD ExcelDataEndRow INT NULL;
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('EmbalseConfig') AND name = 'IsTaponType')
                    ALTER TABLE EmbalseConfig ADD IsTaponType BIT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('EmbalseConfig') AND name = 'TotalUnits')
                    ALTER TABLE EmbalseConfig ADD TotalUnits INT NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('EmbalseConfig') AND name = 'BhgKey')
                    ALTER TABLE EmbalseConfig ADD BhgKey NVARCHAR(100) NULL;

                -- Seed extended columns for existing rows that have NULLs
                UPDATE EmbalseConfig SET Color='#1565C0', HydroKey='Angostura', CuencaCode='ang', ExcelHeaderRow=12, ExcelDataStartRow=15, ExcelDataEndRow=38, IsTaponType=0, TotalUnits=5, BhgKey='ANGOSTURA' WHERE PresaKey='Angostura' AND HydroKey IS NULL;
                UPDATE EmbalseConfig SET Color='#42a5f5', HydroKey='Chicoasen', CuencaCode='mmt', ExcelHeaderRow=45, ExcelDataStartRow=48, ExcelDataEndRow=70, IsTaponType=0, TotalUnits=8, BhgKey='CHICOASEN' WHERE PresaKey='Chicoasen' AND HydroKey IS NULL;
                UPDATE EmbalseConfig SET Color='#00838F', HydroKey='Malpaso', CuencaCode='mps', ExcelHeaderRow=78, ExcelDataStartRow=81, ExcelDataEndRow=103, IsTaponType=0, TotalUnits=6, BhgKey='MALPASO' WHERE PresaKey='Malpaso' AND HydroKey IS NULL;
                UPDATE EmbalseConfig SET Color='#ff7043', HydroKey='JGrijalva', ExcelHeaderRow=111, ExcelDataStartRow=114, ExcelDataEndRow=136, IsTaponType=1, TotalUnits=0, BhgKey='CANAL_JG' WHERE PresaKey='Tapon_Juan_Grijalva' AND HydroKey IS NULL;
                UPDATE EmbalseConfig SET Color='#AD1457', HydroKey='Penitas', CuencaCode='pea', ExcelHeaderRow=144, ExcelDataStartRow=147, ExcelDataEndRow=170, IsTaponType=0, TotalUnits=4, BhgKey='PEÑITAS' WHERE PresaKey='Penitas' AND HydroKey IS NULL;
            ");
        }

        // GET: /FunVasos/GetEmbalseConfig — solo activos
        [HttpGet]
        public async Task<IActionResult> GetEmbalseConfig()
        {
            try
            {
                await EnsureEmbalseConfigTableAsync();
                using var db = new SqlConnection(_sqlConn);
                var configs = await db.QueryAsync<EmbalseConfigDto>(
                    "SELECT Id, PresaKey, NombreDisplay, Namo, Name, Namino, IsActive, SortOrder, Color, HydroKey, CuencaCode, ExcelHeaderRow, ExcelDataStartRow, ExcelDataEndRow, IsTaponType, TotalUnits, BhgKey FROM EmbalseConfig WHERE IsActive = 1 ORDER BY SortOrder");
                return Json(new { success = true, data = configs });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: /FunVasos/GetEmbalseConfigAll — todos incluido inactivos
        [HttpGet]
        public async Task<IActionResult> GetEmbalseConfigAll()
        {
            try
            {
                await EnsureEmbalseConfigTableAsync();
                using var db = new SqlConnection(_sqlConn);
                var configs = await db.QueryAsync<EmbalseConfigDto>(
                    "SELECT Id, PresaKey, NombreDisplay, Namo, Name, Namino, IsActive, SortOrder, Color, HydroKey, CuencaCode, ExcelHeaderRow, ExcelDataStartRow, ExcelDataEndRow, IsTaponType, TotalUnits, BhgKey FROM EmbalseConfig ORDER BY SortOrder");
                return Json(new { success = true, data = configs });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /FunVasos/SaveEmbalseConfig
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveEmbalseConfig([FromBody] EmbalseConfigDto model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.PresaKey))
                return Json(new { success = false, message = "PresaKey es requerido" });

            try
            {
                await EnsureEmbalseConfigTableAsync();
                using var db = new SqlConnection(_sqlConn);

                if (model.Id > 0)
                {
                    await db.ExecuteAsync(@"
                        UPDATE EmbalseConfig 
                        SET NombreDisplay = @NombreDisplay, Namo = @Namo, Name = @Name, Namino = @Namino,
                            IsActive = @IsActive, SortOrder = @SortOrder, Color = @Color,
                            HydroKey = @HydroKey, CuencaCode = @CuencaCode,
                            ExcelHeaderRow = @ExcelHeaderRow, ExcelDataStartRow = @ExcelDataStartRow,
                            ExcelDataEndRow = @ExcelDataEndRow, IsTaponType = @IsTaponType,
                            TotalUnits = @TotalUnits, BhgKey = @BhgKey,
                            UsuarioModifica = @User, FechaModifica = GETDATE()
                        WHERE Id = @Id",
                        new { model.Id, model.NombreDisplay, model.Namo, model.Name, model.Namino,
                              model.IsActive, model.SortOrder, model.Color, model.HydroKey, model.CuencaCode,
                              model.ExcelHeaderRow, model.ExcelDataStartRow, model.ExcelDataEndRow,
                              model.IsTaponType, model.TotalUnits, model.BhgKey,
                              User = User.Identity?.Name ?? "Admin" });
                }
                else
                {
                    // Check duplicate PresaKey
                    var exists = await db.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM EmbalseConfig WHERE PresaKey = @PresaKey", new { model.PresaKey });
                    if (exists > 0)
                        return Json(new { success = false, message = $"Ya existe configuración para '{model.PresaKey}'" });

                    model.Id = await db.ExecuteScalarAsync<long>(@"
                        INSERT INTO EmbalseConfig (PresaKey, NombreDisplay, Namo, Name, Namino, IsActive, SortOrder, Color, HydroKey, CuencaCode, ExcelHeaderRow, ExcelDataStartRow, ExcelDataEndRow, IsTaponType, TotalUnits, BhgKey, UsuarioModifica)
                        OUTPUT INSERTED.Id
                        VALUES (@PresaKey, @NombreDisplay, @Namo, @Name, @Namino, @IsActive, @SortOrder, @Color, @HydroKey, @CuencaCode, @ExcelHeaderRow, @ExcelDataStartRow, @ExcelDataEndRow, @IsTaponType, @TotalUnits, @BhgKey, @User)",
                        new { model.PresaKey, model.NombreDisplay, model.Namo, model.Name, model.Namino,
                              model.IsActive, model.SortOrder, model.Color, model.HydroKey, model.CuencaCode,
                              model.ExcelHeaderRow, model.ExcelDataStartRow, model.ExcelDataEndRow,
                              model.IsTaponType, model.TotalUnits, model.BhgKey,
                              User = User.Identity?.Name ?? "Admin" });
                }
                return Json(new { success = true, config = model });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /FunVasos/ToggleEmbalse
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleEmbalse([FromBody] EmbalseConfigDto model)
        {
            if (model == null || model.Id <= 0)
                return Json(new { success = false, message = "ID inválido" });

            try
            {
                using var db = new SqlConnection(_sqlConn);
                await db.ExecuteAsync(
                    "UPDATE EmbalseConfig SET IsActive = @IsActive, FechaModifica = GETDATE() WHERE Id = @Id",
                    new { model.Id, model.IsActive });
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}
