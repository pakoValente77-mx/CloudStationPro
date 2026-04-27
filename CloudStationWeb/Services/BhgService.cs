using Npgsql;
using Dapper;
using OfficeOpenXml;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CloudStationWeb.Services
{
    public class BhgPresaDiario
    {
        public DateTime Ts { get; set; }
        public string Presa { get; set; } = "";
        public double? Nivel { get; set; }
        public double? CurvaGuia { get; set; }
        public double? DiffCurvaGuia { get; set; }
        public double? VolAlmacenado { get; set; }
        public double? PctLlenadoNamo { get; set; }
        public double? PctLlenadoName { get; set; }
        public double? AportacionVol { get; set; }
        public double? AportacionQ { get; set; }
        public double? ExtraccionVol { get; set; }
        public double? ExtraccionQ { get; set; }
        public double? GeneracionGwh { get; set; }
        public double? FactorPlanta { get; set; }
    }

    public class BhgEstacionDiario
    {
        public DateTime Ts { get; set; }
        public string Estacion { get; set; } = "";
        public string? Subcuenca { get; set; }
        public double? Precip24h { get; set; }
        public double? PrecipAcumMensual { get; set; }
        public double? Escala { get; set; }
        public double? Gasto { get; set; }
        public double? Evaporacion { get; set; }
        public double? TempMax { get; set; }
        public double? TempMin { get; set; }
        public double? TempAmb { get; set; }
    }

    public class BhgArchivo
    {
        public long Id { get; set; }
        public DateTime Fecha { get; set; }
        public string NombreArchivo { get; set; } = "";
        public DateTime ProcesadoTs { get; set; }
        public int Mes { get; set; }
        public int Anio { get; set; }
        public int DiasConDatos { get; set; }
        public int NumEstaciones { get; set; }
    }

    public class BhgImportResult
    {
        public int PresaRows { get; set; }
        public int EstacionRows { get; set; }
        public DateTime Fecha { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public class BhgViewModel
    {
        public int Mes { get; set; }
        public int Anio { get; set; }
        public string MesNombre { get; set; } = "";
        public List<BhgPresaDiario> Presas { get; set; } = new();
        public List<BhgEstacionDiario> Estaciones { get; set; } = new();
        public List<BhgArchivo> Archivos { get; set; } = new();
        public List<string> PresasDisponibles { get; set; } = new();
        public List<string> SubcuencasDisponibles { get; set; } = new();
    }

    public class BhgService
    {
        private readonly string _pgConn;

        private static readonly Dictionary<int, (string Estacion, string Subcuenca)> EstacionesBoletin = new()
        {
            [11] = ("LA ANGOSTURA", "ANGOSTURA"),
            [12] = ("PTE. CONCORDIA", "ANGOSTURA"),
            [13] = ("SAN MIGUEL", "ANGOSTURA"),
            [14] = ("REFORMA", "ANGOSTURA"),
            [15] = ("REV. MEXICANA", "ANGOSTURA"),
            [18] = ("CHICOASEN", "CHICOASEN"),
            [19] = ("STO. DOMINGO", "CHICOASEN"),
            [20] = ("EL BOQUERON", "CHICOASEN"),
            [21] = ("ACALA", "CHICOASEN"),
            [22] = ("TUXTLA", "CHICOASEN"),
            [23] = ("SIERRA MORENA", "CHICOASEN"),
            [26] = ("VERTEDORES MALPASO", "MALPASO"),
            [27] = ("POBLADO CHICOASEN", "MALPASO"),
            [28] = ("LAS FLORES II", "MALPASO"),
            [29] = ("STA. MARIA", "MALPASO"),
            [30] = ("YAMONHO", "MALPASO"),
            [33] = ("VERTEDORES PEÑITAS", "PEÑITAS"),
            [34] = ("TZIMBAC", "PEÑITAS"),
            [35] = ("SAYULA", "PEÑITAS"),
            [36] = ("OCOTEPEC", "PEÑITAS"),
            [37] = ("JUAN GRIJALVA SUP.", "PEÑITAS"),
            [41] = ("SAMARIA", "BAJO GRIJALVA"),
            [42] = ("GONZALEZ", "BAJO GRIJALVA"),
            [45] = ("VILLAHERMOSA", "BAJO GRIJALVA"),
            [48] = ("OXOLOTAN", "TACOTALPA"),
            [52] = ("BOCA DEL CERRO", "USUMACINTA"),
        };

        private static readonly Dictionary<string, (int Nivel, int? DiffCg)> NivelesCols = new()
        {
            ["ANGOSTURA"] = (2, 3),
            ["CHICOASEN"] = (4, null),
            ["MALPASO"] = (5, 6),
            ["CANAL_JG"] = (7, null),
            ["PEÑITAS"] = (8, null),
        };

        private static readonly Dictionary<string, (int Vol, int Q)> AportExtracCols = new()
        {
            ["ANGOSTURA"] = (2, 3),
            ["CHICOASEN"] = (4, 5),
            ["MALPASO"] = (6, 7),
            ["PEÑITAS"] = (8, 9),
        };

        private static readonly Dictionary<string, (int Gen, int Fp)> GeneracionCols = new()
        {
            ["ANGOSTURA"] = (2, 3),
            ["CHICOASEN"] = (4, 5),
            ["MALPASO"] = (6, 7),
            ["PEÑITAS"] = (8, 9),
        };

        private static readonly string[] MesesEs = {
            "", "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
            "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
        };

        static BhgService()
        {
            Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        }

        public BhgService(IConfiguration config)
        {
            _pgConn = config.GetConnectionString("PostgreSQL")
                ?? config["TimescaleDB:ConnectionString"]
                ?? $"Host={config["timescaledb:host"]};Port={config["timescaledb:port"]};Database={config["timescaledb:database"]};Username={config["timescaledb:user"]};Password={config["timescaledb:password"]}";
        }

        public async Task<BhgViewModel> GetDataAsync(int? mes = null, int? anio = null)
        {
            var now = DateTime.Now;
            int m = mes ?? now.Month;
            int a = anio ?? now.Year;

            var vm = new BhgViewModel
            {
                Mes = m,
                Anio = a,
                MesNombre = m >= 1 && m <= 12 ? MesesEs[m] : "?"
            };

            using var db = new NpgsqlConnection(_pgConn);
            await db.OpenAsync();

            // Presas del mes
            vm.Presas = (await db.QueryAsync<BhgPresaDiario>(@"
                SELECT ts::timestamp, presa, nivel, curva_guia, diff_curva_guia,
                       vol_almacenado, pct_llenado_namo, pct_llenado_name,
                       aportacion_vol, aportacion_q, extraccion_vol, extraccion_q,
                       generacion_gwh, factor_planta
                FROM bhg_presa_diario
                WHERE EXTRACT(MONTH FROM ts) = @Mes AND EXTRACT(YEAR FROM ts) = @Anio
                ORDER BY ts, presa",
                new { Mes = m, Anio = a })).ToList();

            // Estaciones del mes
            vm.Estaciones = (await db.QueryAsync<BhgEstacionDiario>(@"
                SELECT ts::timestamp, estacion, subcuenca,
                       precip_24h, precip_acum_mensual,
                       escala, gasto, evaporacion,
                       temp_max, temp_min, temp_amb
                FROM bhg_estacion_diario
                WHERE EXTRACT(MONTH FROM ts) = @Mes AND EXTRACT(YEAR FROM ts) = @Anio
                ORDER BY ts, estacion",
                new { Mes = m, Anio = a })).ToList();

            // Archivos procesados
            vm.Archivos = (await db.QueryAsync<BhgArchivo>(@"
                SELECT id, fecha::timestamp, nombre_archivo, procesado_ts, mes, anio, dias_con_datos, num_estaciones
                FROM bhg_archivo
                ORDER BY fecha DESC
                LIMIT 50")).ToList();

            vm.PresasDisponibles = vm.Presas.Select(p => p.Presa).Distinct().OrderBy(p => p).ToList();
            vm.SubcuencasDisponibles = vm.Estaciones
                .Where(e => !string.IsNullOrEmpty(e.Subcuenca))
                .Select(e => e.Subcuenca!).Distinct().OrderBy(s => s).ToList();

            return vm;
        }

        public async Task<List<DateTime>> GetAvailableMonthsAsync()
        {
            using var db = new NpgsqlConnection(_pgConn);
            return (await db.QueryAsync<DateTime>(@"
                SELECT DISTINCT DATE_TRUNC('month', fecha) AS mes
                FROM bhg_archivo
                ORDER BY mes DESC")).ToList();
        }

        public async Task<BhgImportResult> ParseAndStoreAsync(string filePath, string? originalFileName = null)
        {
            var result = new BhgImportResult { Fecha = DateTime.UtcNow.Date };
            ExcelPackage.License.SetNonCommercialOrganization("CFE");

            using var package = new ExcelPackage(new FileInfo(filePath));
            var presasData = new Dictionary<(int Dia, string Presa), BhgPresaDiario>();
            var estacionesData = new List<BhgEstacionDiario>();

            var boletin = package.Workbook.Worksheets["Boletín"];
            DateTime? fecha = null;
            if (boletin != null)
            {
                fecha = GetBhgDate(boletin);
                if (fecha.HasValue)
                {
                    var dia = fecha.Value.Day;
                    var presaCols = new Dictionary<string, int>
                    {
                        ["ANGOSTURA"] = 3,
                        ["CHICOASEN"] = 4,
                        ["MALPASO"] = 5,
                        ["CANAL_JG"] = 6,
                        ["PEÑITAS"] = 7,
                    };

                    foreach (var (presa, col) in presaCols)
                    {
                        var row = GetPresaRecord(presasData, dia, presa);
                        row.Nivel = GetCellDouble(boletin, 52, col);
                        row.VolAlmacenado = GetCellDouble(boletin, 53, col);
                        row.PctLlenadoNamo = GetCellDouble(boletin, 55, col);
                        row.PctLlenadoName = GetCellDouble(boletin, 57, col);
                    }

                    foreach (var (excelRow, station) in EstacionesBoletin)
                    {
                        var est = new BhgEstacionDiario
                        {
                            Ts = fecha.Value,
                            Estacion = station.Estacion,
                            Subcuenca = station.Subcuenca,
                            Precip24h = GetCellDouble(boletin, excelRow, 10),
                            Escala = GetCellDouble(boletin, excelRow, 11),
                            Gasto = GetCellDouble(boletin, excelRow, 12),
                            Evaporacion = GetCellDouble(boletin, excelRow, 13),
                            TempMax = GetCellDouble(boletin, excelRow, 14),
                            TempMin = GetCellDouble(boletin, excelRow, 15),
                            TempAmb = GetCellDouble(boletin, excelRow, 16),
                        };

                        if (new[] { est.Precip24h, est.Escala, est.Gasto, est.Evaporacion, est.TempMax, est.TempMin, est.TempAmb }.Any(v => v.HasValue))
                            estacionesData.Add(est);
                    }
                }
            }
            else
            {
                result.Errors.Add("No se encontró la hoja 'Boletín'.");
            }

            fecha ??= ExtractDateFromFilename(Path.GetFileName(filePath));
            if (!fecha.HasValue)
            {
                result.Errors.Add("No se pudo extraer la fecha del BHG.");
                return result;
            }

            result.Fecha = fecha.Value.Date;
            var mes = result.Fecha.Month;
            var anio = result.Fecha.Year;

            ParseNiveles(package, presasData);
            ParseAportaciones(package, "Aportaciones", presasData, isExtraccion: false);
            ParseAportaciones(package, "Extracción", presasData, isExtraccion: true);
            ParseGeneracion(package, presasData);
            ParsePrecipitacion(package, estacionesData);

            if (presasData.Count == 0 && estacionesData.Count == 0)
            {
                result.Errors.Add("No se encontraron datos en el archivo BHG.");
                return result;
            }

            await StoreParsedDataAsync(result, mes, anio, presasData, estacionesData, originalFileName ?? Path.GetFileName(filePath));
            return result;
        }

        public async Task<(int presaRows, int estRows, string? error)> StoreFromUploadAsync(string tempFilePath)
        {
            try
            {
                var result = await ParseAndStoreAsync(tempFilePath);
                return (result.PresaRows, result.EstacionRows, result.Errors.Any() ? string.Join("; ", result.Errors) : null);
            }
            catch (Exception ex)
            {
                return (0, 0, ex.Message);
            }
        }

        private static BhgPresaDiario GetPresaRecord(Dictionary<(int Dia, string Presa), BhgPresaDiario> rows, int dia, string presa)
        {
            var key = (dia, presa);
            if (!rows.TryGetValue(key, out var row))
            {
                row = new BhgPresaDiario { Presa = presa };
                rows[key] = row;
            }
            return row;
        }

        private static double? GetCellDouble(ExcelWorksheet ws, int row, int col)
        {
            var value = ws.Cells[row, col].Value;
            if (value == null) return null;
            if (value is double d) return d;
            if (value is int i) return i;
            if (value is decimal m) return (double)m;
            if (value is DateTime) return null;

            var text = value.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(text) || text.Contains('#')) return null;
            text = text.Replace(",", "");
            return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        private static DateTime? GetBhgDate(ExcelWorksheet ws)
        {
            var value = ws.Cells[4, 14].Value;
            if (value is DateTime dt) return dt.Date;
            if (value is double oa)
            {
                try { return DateTime.FromOADate(oa).Date; }
                catch { return null; }
            }

            var text = value?.ToString();
            return DateTime.TryParse(text, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out var parsed)
                ? parsed.Date
                : null;
        }

        private static DateTime? ExtractDateFromFilename(string filename)
        {
            var match = Regex.Match(filename, @"bhg(\d{2})(\d{2})(\d{2})", RegexOptions.IgnoreCase);
            if (!match.Success) return null;

            var day = int.Parse(match.Groups[1].Value);
            var month = int.Parse(match.Groups[2].Value);
            var year = 2000 + int.Parse(match.Groups[3].Value);
            try { return new DateTime(year, month, day); }
            catch { return null; }
        }

        private static void ParseNiveles(ExcelPackage package, Dictionary<(int Dia, string Presa), BhgPresaDiario> presasData)
        {
            var ws = package.Workbook.Worksheets["Niveles"];
            if (ws == null) return;

            for (int row = 9; row <= 39; row++)
            {
                var diaVal = GetCellDouble(ws, row, 1);
                if (!diaVal.HasValue || diaVal < 1 || diaVal > 31) continue;
                var dia = (int)diaVal.Value;

                foreach (var (presa, cols) in NivelesCols)
                {
                    var nivel = GetCellDouble(ws, row, cols.Nivel);
                    if (!nivel.HasValue) continue;

                    var record = GetPresaRecord(presasData, dia, presa);
                    record.Nivel = nivel;
                    if (cols.DiffCg.HasValue)
                        record.DiffCurvaGuia = GetCellDouble(ws, row, cols.DiffCg.Value);
                }

                var cgAng = GetCellDouble(ws, row, 36);
                if (cgAng.HasValue && presasData.TryGetValue((dia, "ANGOSTURA"), out var ang))
                    ang.CurvaGuia = cgAng;

                var cgMps = GetCellDouble(ws, row, 39);
                if (cgMps.HasValue && presasData.TryGetValue((dia, "MALPASO"), out var mps))
                    mps.CurvaGuia = cgMps;
            }
        }

        private static void ParseAportaciones(ExcelPackage package, string sheetName, Dictionary<(int Dia, string Presa), BhgPresaDiario> presasData, bool isExtraccion)
        {
            var ws = package.Workbook.Worksheets[sheetName];
            if (ws == null) return;

            for (int row = 10; row <= 41; row++)
            {
                var diaVal = GetCellDouble(ws, row, 1);
                if (!diaVal.HasValue || diaVal < 1 || diaVal > 31) continue;
                var dia = (int)diaVal.Value;

                foreach (var (presa, cols) in AportExtracCols)
                {
                    var vol = GetCellDouble(ws, row, cols.Vol);
                    var q = GetCellDouble(ws, row, cols.Q);
                    if (!vol.HasValue && !q.HasValue) continue;

                    var record = GetPresaRecord(presasData, dia, presa);
                    if (isExtraccion)
                    {
                        record.ExtraccionVol = vol;
                        record.ExtraccionQ = q;
                    }
                    else
                    {
                        record.AportacionVol = vol;
                        record.AportacionQ = q;
                    }
                }
            }
        }

        private static void ParseGeneracion(ExcelPackage package, Dictionary<(int Dia, string Presa), BhgPresaDiario> presasData)
        {
            var ws = package.Workbook.Worksheets.FirstOrDefault(s =>
                s.Name.Contains("eneración", StringComparison.OrdinalIgnoreCase) ||
                s.Name.Contains("eneracion", StringComparison.OrdinalIgnoreCase));
            if (ws == null) return;

            for (int row = 10; row <= 41; row++)
            {
                var diaVal = GetCellDouble(ws, row, 1);
                if (!diaVal.HasValue || diaVal < 1 || diaVal > 31) continue;
                var dia = (int)diaVal.Value;

                foreach (var (presa, cols) in GeneracionCols)
                {
                    var gen = GetCellDouble(ws, row, cols.Gen);
                    var fp = GetCellDouble(ws, row, cols.Fp);
                    if (!gen.HasValue && !fp.HasValue) continue;

                    var record = GetPresaRecord(presasData, dia, presa);
                    record.GeneracionGwh = gen;
                    record.FactorPlanta = fp;
                }
            }
        }

        private static void ParsePrecipitacion(ExcelPackage package, List<BhgEstacionDiario> estacionesData)
        {
            var ws = package.Workbook.Worksheets["Precipitación"];
            if (ws == null) return;

            for (int row = 10; row <= 38; row++)
            {
                var raw = ws.Cells[row, 47].Value?.ToString();
                var acum = GetCellDouble(ws, row, 48);
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var normalized = raw.Trim().ToUpperInvariant().Replace("*", "").Trim();
                var station = estacionesData.FirstOrDefault(e =>
                    normalized.Contains(e.Estacion, StringComparison.OrdinalIgnoreCase) ||
                    e.Estacion.Contains(normalized, StringComparison.OrdinalIgnoreCase));
                if (station != null)
                    station.PrecipAcumMensual = acum;
            }
        }

        private async Task StoreParsedDataAsync(
            BhgImportResult result,
            int mes,
            int anio,
            Dictionary<(int Dia, string Presa), BhgPresaDiario> presasData,
            List<BhgEstacionDiario> estacionesData,
            string nombreArchivo)
        {
            var diasMax = DateTime.DaysInMonth(anio, mes);
            using var db = new NpgsqlConnection(_pgConn);
            await db.OpenAsync();
            using var tx = await db.BeginTransactionAsync();

            const string upsertPresa = @"
                INSERT INTO public.bhg_presa_diario
                (ts, presa, nivel, curva_guia, diff_curva_guia, vol_almacenado,
                 pct_llenado_namo, pct_llenado_name,
                 aportacion_vol, aportacion_q, extraccion_vol, extraccion_q,
                 generacion_gwh, factor_planta)
                VALUES
                (@Ts, @Presa, @Nivel, @CurvaGuia, @DiffCurvaGuia, @VolAlmacenado,
                 @PctLlenadoNamo, @PctLlenadoName,
                 @AportacionVol, @AportacionQ, @ExtraccionVol, @ExtraccionQ,
                 @GeneracionGwh, @FactorPlanta)
                ON CONFLICT (ts, presa) DO UPDATE SET
                 nivel = COALESCE(EXCLUDED.nivel, public.bhg_presa_diario.nivel),
                 curva_guia = COALESCE(EXCLUDED.curva_guia, public.bhg_presa_diario.curva_guia),
                 diff_curva_guia = COALESCE(EXCLUDED.diff_curva_guia, public.bhg_presa_diario.diff_curva_guia),
                 vol_almacenado = COALESCE(EXCLUDED.vol_almacenado, public.bhg_presa_diario.vol_almacenado),
                 pct_llenado_namo = COALESCE(EXCLUDED.pct_llenado_namo, public.bhg_presa_diario.pct_llenado_namo),
                 pct_llenado_name = COALESCE(EXCLUDED.pct_llenado_name, public.bhg_presa_diario.pct_llenado_name),
                 aportacion_vol = COALESCE(EXCLUDED.aportacion_vol, public.bhg_presa_diario.aportacion_vol),
                 aportacion_q = COALESCE(EXCLUDED.aportacion_q, public.bhg_presa_diario.aportacion_q),
                 extraccion_vol = COALESCE(EXCLUDED.extraccion_vol, public.bhg_presa_diario.extraccion_vol),
                 extraccion_q = COALESCE(EXCLUDED.extraccion_q, public.bhg_presa_diario.extraccion_q),
                 generacion_gwh = COALESCE(EXCLUDED.generacion_gwh, public.bhg_presa_diario.generacion_gwh),
                 factor_planta = COALESCE(EXCLUDED.factor_planta, public.bhg_presa_diario.factor_planta)";

            foreach (var ((dia, _), record) in presasData)
            {
                if (dia < 1 || dia > diasMax) continue;
                record.Ts = new DateTime(anio, mes, dia);
                await db.ExecuteAsync(upsertPresa, record, tx);
                result.PresaRows++;
            }

            const string upsertEstacion = @"
                INSERT INTO public.bhg_estacion_diario
                (ts, estacion, subcuenca, precip_24h, precip_acum_mensual,
                 escala, gasto, evaporacion, temp_max, temp_min, temp_amb)
                VALUES
                (@Ts, @Estacion, @Subcuenca, @Precip24h, @PrecipAcumMensual,
                 @Escala, @Gasto, @Evaporacion, @TempMax, @TempMin, @TempAmb)
                ON CONFLICT (ts, estacion) DO UPDATE SET
                 subcuenca = COALESCE(EXCLUDED.subcuenca, public.bhg_estacion_diario.subcuenca),
                 precip_24h = COALESCE(EXCLUDED.precip_24h, public.bhg_estacion_diario.precip_24h),
                 precip_acum_mensual = COALESCE(EXCLUDED.precip_acum_mensual, public.bhg_estacion_diario.precip_acum_mensual),
                 escala = COALESCE(EXCLUDED.escala, public.bhg_estacion_diario.escala),
                 gasto = COALESCE(EXCLUDED.gasto, public.bhg_estacion_diario.gasto),
                 evaporacion = COALESCE(EXCLUDED.evaporacion, public.bhg_estacion_diario.evaporacion),
                 temp_max = COALESCE(EXCLUDED.temp_max, public.bhg_estacion_diario.temp_max),
                 temp_min = COALESCE(EXCLUDED.temp_min, public.bhg_estacion_diario.temp_min),
                 temp_amb = COALESCE(EXCLUDED.temp_amb, public.bhg_estacion_diario.temp_amb)";

            foreach (var station in estacionesData)
            {
                await db.ExecuteAsync(upsertEstacion, station, tx);
                result.EstacionRows++;
            }

            await db.ExecuteAsync(@"
                INSERT INTO public.bhg_archivo (fecha, nombre_archivo, mes, anio, dias_con_datos, num_estaciones)
                VALUES (@Fecha, @NombreArchivo, @Mes, @Anio, @DiasConDatos, @NumEstaciones)
                ON CONFLICT (fecha) DO UPDATE SET
                    nombre_archivo = EXCLUDED.nombre_archivo,
                    procesado_ts = NOW(),
                    dias_con_datos = EXCLUDED.dias_con_datos,
                    num_estaciones = EXCLUDED.num_estaciones",
                new
                {
                    result.Fecha,
                    NombreArchivo = nombreArchivo,
                    Mes = mes,
                    Anio = anio,
                    DiasConDatos = presasData.Keys.Select(k => k.Dia).Distinct().Count(),
                    NumEstaciones = result.EstacionRows
                },
                tx);

            await tx.CommitAsync();
        }
    }
}
