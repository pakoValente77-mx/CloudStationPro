using System.Data;
using System.Globalization;
using CloudStationWeb.Models;
using Dapper;
using Npgsql;
using OfficeOpenXml;

namespace CloudStationWeb.Services
{
    public class FunVasosService
    {
        private readonly string _postgresConn;

        private static readonly Dictionary<string, (int headerRow, int dataStartRow, int dataEndRow, bool isTapon)> PresaSections = new()
        {
            ["Angostura"]          = (12, 15, 38, false),
            ["Chicoasén"]          = (45, 48, 70, false),
            ["Malpaso"]            = (78, 81, 103, false),
            ["Tapón Juan Grijalva"] = (111, 114, 136, true),
            ["Peñitas"]            = (144, 147, 170, false),
        };

        public FunVasosService(IConfiguration configuration)
        {
            _postgresConn = configuration.GetConnectionString("PostgreSQL") ?? "";
        }

        /// <summary>
        /// Parse an uploaded FIN Excel file and insert hourly data into TimescaleDB.
        /// Returns (rowsInserted, date, errors).
        /// </summary>
        public async Task<(int rowsInserted, DateTime date, List<string> errors)> ParseAndStoreAsync(string filePath)
        {
            var errors = new List<string>();
            int totalInserted = 0;
            DateTime reportDate = DateTime.UtcNow.Date;

            ExcelPackage.License.SetNonCommercialOrganization("CFE");

            using var package = new ExcelPackage(new FileInfo(filePath));
            var ws = package.Workbook.Worksheets["FIN"];
            if (ws == null)
            {
                errors.Add("No se encontró la hoja 'FIN' en el archivo Excel.");
                return (0, reportDate, errors);
            }

            // Extract date from row 4, column H (or row 3)
            reportDate = ExtractDate(ws);

            var allRows = new List<FunVasosHorario>();

            foreach (var (presa, (headerRow, dataStart, dataEnd, isTapon)) in PresaSections)
            {
                for (int row = dataStart; row <= dataEnd; row++)
                {
                    var horaVal = GetCellFloat(ws, row, 1); // Column A = Hora
                    if (horaVal == null || horaVal < 1 || horaVal > 24) continue;

                    short hora = (short)horaVal.Value;

                    // Check if elevation has data (skip empty rows)
                    var elev = GetCellFloat(ws, row, 2); // Column B
                    if (elev == null) continue;

                    var record = new FunVasosHorario
                    {
                        Ts = reportDate,
                        Presa = presa,
                        Hora = hora,
                        Elevacion = elev,
                        Almacenamiento = GetCellFloat(ws, row, 3),    // C
                        Diferencia = GetCellFloat(ws, row, 4),        // D
                        AportacionesQ = GetCellFloat(ws, row, 5),     // E
                        AportacionesV = GetCellFloat(ws, row, 6),     // F
                        ExtraccionesTurbQ = GetCellFloat(ws, row, 7), // G (Turbinas or Canal)
                        ExtraccionesTurbV = GetCellFloat(ws, row, 8), // H
                        ExtraccionesVertQ = GetCellFloat(ws, row, 9), // I (Vertedor or Túneles)
                        ExtraccionesVertV = GetCellFloat(ws, row, 10),// J
                        ExtraccionesTotalQ = GetCellFloat(ws, row, 11),// K
                        ExtraccionesTotalV = GetCellFloat(ws, row, 12),// L
                        Generacion = isTapon ? null : GetCellFloat(ws, row, 13), // M (Tapón no tiene)
                        NumUnidades = isTapon ? null : GetCellShort(ws, row, 14), // N
                        AportacionCuencaPropia = GetCellFloat(ws, row, 15), // O
                        AportacionPromedio = GetCellFloat(ws, row, 16),     // P
                    };

                    allRows.Add(record);
                }
            }

            if (allRows.Count == 0)
            {
                errors.Add("No se encontraron datos horarios en el archivo.");
                return (0, reportDate, errors);
            }

            // Insert into TimescaleDB
            using var conn = new NpgsqlConnection(_postgresConn);
            await conn.OpenAsync();

            // Delete existing data for this date (upsert behavior)
            await conn.ExecuteAsync(
                "DELETE FROM public.funvasos_horario WHERE ts = @Ts",
                new { Ts = reportDate });

            const string insertSql = @"
                INSERT INTO public.funvasos_horario 
                (ts, presa, hora, elevacion, almacenamiento, diferencia,
                 aportaciones_q, aportaciones_v, extracciones_turb_q, extracciones_turb_v,
                 extracciones_vert_q, extracciones_vert_v, extracciones_total_q, extracciones_total_v,
                 generacion, num_unidades, aportacion_cuenca_propia, aportacion_promedio)
                VALUES 
                (@Ts, @Presa, @Hora, @Elevacion, @Almacenamiento, @Diferencia,
                 @AportacionesQ, @AportacionesV, @ExtraccionesTurbQ, @ExtraccionesTurbV,
                 @ExtraccionesVertQ, @ExtraccionesVertV, @ExtraccionesTotalQ, @ExtraccionesTotalV,
                 @Generacion, @NumUnidades, @AportacionCuencaPropia, @AportacionPromedio)";

            using var tx = await conn.BeginTransactionAsync();
            foreach (var r in allRows)
            {
                await conn.ExecuteAsync(insertSql, r, tx);
            }
            await tx.CommitAsync();
            totalInserted = allRows.Count;

            return (totalInserted, reportDate, errors);
        }

        /// <summary>
        /// Get all available dates with data.
        /// </summary>
        public async Task<List<DateTime>> GetAvailableDatesAsync()
        {
            using var conn = new NpgsqlConnection(_postgresConn);
            var dates = await conn.QueryAsync<DateTime>(
                "SELECT DISTINCT ts FROM public.funvasos_horario ORDER BY ts DESC");
            return dates.ToList();
        }

        /// <summary>
        /// Get data for a date range.
        /// </summary>
        public async Task<FunVasosViewModel> GetDataAsync(DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            using var conn = new NpgsqlConnection(_postgresConn);

            var dates = await GetAvailableDatesAsync();

            var startDate = fechaInicio ?? dates.FirstOrDefault();
            var endDate = fechaFin ?? startDate;
            if (startDate == default)
            {
                return new FunVasosViewModel
                {
                    FechaInicio = DateTime.UtcNow.Date,
                    FechaFin = DateTime.UtcNow.Date,
                    FechasDisponibles = dates
                };
            }
            if (endDate < startDate) endDate = startDate;

            var rows = (await conn.QueryAsync<FunVasosHorario>(
                @"SELECT ts, presa, hora, elevacion, almacenamiento, diferencia,
                         aportaciones_q AS AportacionesQ, aportaciones_v AS AportacionesV, 
                         extracciones_turb_q AS ExtraccionesTurbQ, extracciones_turb_v AS ExtraccionesTurbV,
                         extracciones_vert_q AS ExtraccionesVertQ, extracciones_vert_v AS ExtraccionesVertV, 
                         extracciones_total_q AS ExtraccionesTotalQ, extracciones_total_v AS ExtraccionesTotalV,
                         generacion, num_unidades AS NumUnidades, 
                         aportacion_cuenca_propia AS AportacionCuencaPropia, aportacion_promedio AS AportacionPromedio
                  FROM public.funvasos_horario 
                  WHERE ts >= @Start AND ts <= @End
                  ORDER BY presa, ts, hora",
                new { Start = startDate, End = endDate })).ToList();

            var presaOrder = new[] { "Angostura", "Chicoasén", "Malpaso", "Tapón Juan Grijalva", "Peñitas" };

            var presas = rows
                .GroupBy(r => r.Presa)
                .Select(g =>
                {
                    var datos = g.OrderBy(x => x.Ts).ThenBy(x => x.Hora).ToList();
                    var last = datos.LastOrDefault();
                    return new FunVasosResumenPresa
                    {
                        Presa = g.Key,
                        Datos = datos,
                        UltimaElevacion = last?.Elevacion,
                        UltimoAlmacenamiento = last?.Almacenamiento,
                        TotalAportacionesV = datos.LastOrDefault()?.AportacionesV,
                        TotalExtraccionesV = datos.LastOrDefault()?.ExtraccionesTotalV,
                        TotalGeneracion = datos.Sum(d => d.Generacion ?? 0),
                        UltimaHora = last?.Hora ?? 0
                    };
                })
                .OrderBy(p => Array.IndexOf(presaOrder, p.Presa) >= 0 ? Array.IndexOf(presaOrder, p.Presa) : 99)
                .ToList();

            return new FunVasosViewModel
            {
                FechaInicio = startDate,
                FechaFin = endDate,
                Presas = presas,
                FechasDisponibles = dates
            };
        }

        /// <summary>
        /// Extract the report date from a FIN Excel file without processing data.
        /// Used to validate the file before upload.
        /// </summary>
        public DateTime? ExtractDateFromFile(string filePath)
        {
            try
            {
                ExcelPackage.License.SetNonCommercialOrganization("CFE");
                using var package = new ExcelPackage(new FileInfo(filePath));
                var ws = package.Workbook.Worksheets["FIN"];
                if (ws == null) return null;
                return ExtractDate(ws);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Try to extract date from filename pattern FIN{DDMMYY}.xlsx
        /// </summary>
        public static DateTime? ExtractDateFromFilename(string filename)
        {
            // Pattern: FIN240326.xlsx → 24/03/26
            var name = Path.GetFileNameWithoutExtension(filename);
            if (name != null && name.StartsWith("FIN", StringComparison.OrdinalIgnoreCase) && name.Length >= 9)
            {
                var datePart = name.Substring(3, 6); // DDMMYY
                if (int.TryParse(datePart.Substring(0, 2), out int day)
                    && int.TryParse(datePart.Substring(2, 2), out int month)
                    && int.TryParse(datePart.Substring(4, 2), out int year))
                {
                    year += 2000;
                    try { return new DateTime(year, month, day); } catch { }
                }
            }
            return null;
        }

        private DateTime ExtractDate(ExcelWorksheet ws)
        {
            // Try row 4, column H (datetime value)
            var dateCell = ws.Cells[4, 8].Value;
            if (dateCell is DateTime dt) return dt.Date;

            // Try row 3, column H (text like "24 de Marzo de 2026")
            var dateText = ws.Cells[3, 8].Value?.ToString();
            if (!string.IsNullOrEmpty(dateText))
            {
                // Try parsing Spanish date format
                var cultures = new[] { new CultureInfo("es-MX"), new CultureInfo("es-ES") };
                foreach (var culture in cultures)
                {
                    if (DateTime.TryParse(dateText, culture, DateTimeStyles.None, out var parsed))
                        return parsed.Date;
                }

                // Manual parse "DD de MES de YYYY"
                var parts = dateText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5)
                {
                    var months = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["enero"] = 1, ["febrero"] = 2, ["marzo"] = 3, ["abril"] = 4,
                        ["mayo"] = 5, ["junio"] = 6, ["julio"] = 7, ["agosto"] = 8,
                        ["septiembre"] = 9, ["octubre"] = 10, ["noviembre"] = 11, ["diciembre"] = 12
                    };

                    if (int.TryParse(parts[0], out int day)
                        && months.TryGetValue(parts[2], out int month)
                        && int.TryParse(parts[4], out int year))
                    {
                        return new DateTime(year, month, day);
                    }
                }
            }

            // Fallback: try filename
            return DateTime.UtcNow.Date;
        }

        private static float? GetCellFloat(ExcelWorksheet ws, int row, int col)
        {
            var val = ws.Cells[row, col].Value;
            if (val == null) return null;
            if (val is double d) return (float)d;
            if (val is float f) return f;
            if (val is int i) return i;
            if (val is long l) return l;
            if (float.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out float result))
                return result;
            return null;
        }

        private static short? GetCellShort(ExcelWorksheet ws, int row, int col)
        {
            var val = ws.Cells[row, col].Value;
            if (val == null) return null;
            if (val is double d) return (short)d;
            if (val is int i) return (short)i;
            if (short.TryParse(val.ToString(), out short result))
                return result;
            return null;
        }
    }
}
