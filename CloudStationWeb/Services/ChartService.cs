using Dapper;
using Npgsql;
using ScottPlot;

namespace CloudStationWeb.Services
{
    public class ChartService
    {
        private readonly string _pgConn;
        private readonly string _sqlConn;
        private readonly string _imageStorePath;
        private readonly string _imageStoreBaseUrl;
        private readonly ILogger<ChartService> _logger;

        public ChartService(IConfiguration config, ILogger<ChartService> logger)
        {
            _pgConn = config.GetConnectionString("PostgreSQL") ?? "";
            _sqlConn = config.GetConnectionString("SqlServer") ?? "";
            _imageStorePath = config["ImageStore:Path"] ?? Path.Combine(AppContext.BaseDirectory, "ImageStore");
            _imageStoreBaseUrl = config["ImageStore:BaseUrl"] ?? "";
            _logger = logger;
        }

        /// <summary>
        /// Generate a dam levels chart (elevación over last 3 days)
        /// </summary>
        public async Task<BotResponse> GenerateDamChartAsync(string? presaFilter = null)
        {
            try
            {
                using var db = new NpgsqlConnection(_pgConn);
                string sql;
                object param;

                if (!string.IsNullOrEmpty(presaFilter))
                {
                    sql = @"SELECT presa, ts + make_interval(hours => hora) AS timestamp, elevacion
                            FROM funvasos_horario
                            WHERE ts >= (SELECT MAX(ts) FROM funvasos_horario) - INTERVAL '3 days'
                              AND elevacion IS NOT NULL AND elevacion > 0
                              AND LOWER(presa) LIKE @Filter
                            ORDER BY presa, ts, hora";
                    param = new { Filter = $"%{presaFilter.ToLower()}%" };
                }
                else
                {
                    sql = @"SELECT presa, ts + make_interval(hours => hora) AS timestamp, elevacion
                            FROM funvasos_horario
                            WHERE ts >= (SELECT MAX(ts) FROM funvasos_horario) - INTERVAL '3 days'
                              AND elevacion IS NOT NULL AND elevacion > 0
                            ORDER BY presa, ts, hora";
                    param = new { };
                }

                var data = (await db.QueryAsync<dynamic>(sql, param)).ToList();
                if (!data.Any())
                    return new BotResponse { Message = "⚠️ No se encontraron datos de elevación para generar la gráfica." };

                var myPlot = new ScottPlot.Plot();
                ApplyDarkTheme(myPlot);

                var groups = data.GroupBy(d => (string)d.presa);
                foreach (var g in groups)
                {
                    var timestamps = g.Select(d => ((DateTime)d.timestamp).ToOADate()).ToArray();
                    var values = g.Select(d => (double)d.elevacion).ToArray();
                    if (timestamps.Length < 2) continue;
                    var scatter = myPlot.Add.Scatter(timestamps, values);
                    scatter.LegendText = g.Key;
                    scatter.LineWidth = 2;
                    scatter.MarkerSize = 0;
                }

                myPlot.Axes.DateTimeTicksBottom();
                myPlot.Title("Elevación de Embalses (msnm)");
                myPlot.YLabel("Elevación (msnm)");
                myPlot.XLabel("Fecha/Hora");
                myPlot.ShowLegend(Alignment.UpperRight);

                return await SaveAndUploadChart(myPlot, "chart_elevacion", "📊 Gráfica de elevación de embalses (últimos 3 días)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating dam chart");
                return new BotResponse { Message = $"⚠️ Error al generar la gráfica de embalses: {ex.Message}" };
            }
        }

        /// <summary>
        /// Generate a generation chart (MW per dam over last 3 days)
        /// </summary>
        public async Task<BotResponse> GenerateGenerationChartAsync(string? presaFilter = null)
        {
            try
            {
                using var db = new NpgsqlConnection(_pgConn);
                string sql;
                object param;

                if (!string.IsNullOrEmpty(presaFilter))
                {
                    sql = @"SELECT presa, ts + make_interval(hours => hora) AS timestamp, generacion
                            FROM funvasos_horario
                            WHERE ts >= (SELECT MAX(ts) FROM funvasos_horario) - INTERVAL '3 days'
                              AND generacion IS NOT NULL AND generacion > 0
                              AND LOWER(presa) LIKE @Filter
                            ORDER BY presa, ts, hora";
                    param = new { Filter = $"%{presaFilter.ToLower()}%" };
                }
                else
                {
                    sql = @"SELECT presa, ts + make_interval(hours => hora) AS timestamp, generacion
                            FROM funvasos_horario
                            WHERE ts >= (SELECT MAX(ts) FROM funvasos_horario) - INTERVAL '3 days'
                              AND generacion IS NOT NULL AND generacion > 0
                            ORDER BY presa, ts, hora";
                    param = new { };
                }

                var data = (await db.QueryAsync<dynamic>(sql, param)).ToList();
                if (!data.Any())
                    return new BotResponse { Message = "⚠️ No se encontraron datos de generación para graficar." };

                var myPlot = new ScottPlot.Plot();
                ApplyDarkTheme(myPlot);

                var groups = data.GroupBy(d => (string)d.presa);
                foreach (var g in groups)
                {
                    var timestamps = g.Select(d => ((DateTime)d.timestamp).ToOADate()).ToArray();
                    var values = g.Select(d => (double)d.generacion).ToArray();
                    if (timestamps.Length < 2) continue;
                    var scatter = myPlot.Add.Scatter(timestamps, values);
                    scatter.LegendText = g.Key;
                    scatter.LineWidth = 2;
                    scatter.MarkerSize = 0;
                }

                myPlot.Axes.DateTimeTicksBottom();
                myPlot.Title("Generación Eléctrica (MW)");
                myPlot.YLabel("MW");
                myPlot.XLabel("Fecha/Hora");
                myPlot.ShowLegend(Alignment.UpperRight);

                return await SaveAndUploadChart(myPlot, "chart_generacion", "📊 Gráfica de generación eléctrica (últimos 3 días)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating generation chart");
                return new BotResponse { Message = $"⚠️ Error al generar la gráfica de generación: {ex.Message}" };
            }
        }

        /// <summary>
        /// Generate a precipitation bar chart (top stations last 24h)
        /// </summary>
        public async Task<BotResponse> GeneratePrecipitationChartAsync()
        {
            try
            {
                using var db = new NpgsqlConnection(_pgConn);
                var data = (await db.QueryAsync<dynamic>(@"
                    SELECT dcp_id, acumulado, horas_con_dato
                    FROM lluvia_acumulada
                    WHERE tipo_periodo = '24h' AND acumulado > 0.5
                    ORDER BY acumulado DESC
                    LIMIT 15")).ToList();

                if (!data.Any())
                    return new BotResponse { Message = "⚠️ No hay precipitación significativa en las últimas 24 horas." };

                // Get station names from SQL Server
                var stationNames = new Dictionary<string, string>();
                try
                {
                    using var sqlDb = new Microsoft.Data.SqlClient.SqlConnection(_sqlConn);
                    var stations = await sqlDb.QueryAsync<dynamic>(@"
                        SELECT e.Nombre, dg.IdSatelital
                        FROM NV_Estacion e
                        INNER JOIN DatosGOES dg ON dg.IdEstacion = e.Id
                        WHERE e.Activo = 1 AND dg.IdSatelital IS NOT NULL");
                    foreach (var s in stations)
                        stationNames[((string)s.IdSatelital).Trim().ToUpper()] = (string)s.Nombre;
                }
                catch { /* fallback to DCP IDs */ }

                var myPlot = new ScottPlot.Plot();
                ApplyDarkTheme(myPlot);

                var labels = data.Select(d =>
                {
                    var dcpId = ((string)d.dcp_id).Trim().ToUpper();
                    var name = stationNames.TryGetValue(dcpId, out var n) ? n : dcpId[..Math.Min(8, dcpId.Length)];
                    return name.Length > 18 ? name[..18] + "…" : name;
                }).ToArray();
                var values = data.Select(d => (double)d.acumulado).ToArray();

                var bars = values.Select((v, i) => new ScottPlot.Bar
                {
                    Position = i,
                    Value = v,
                    FillColor = ScottPlot.Color.FromHex("#00bcd4")
                }).ToArray();
                var barPlot = myPlot.Add.Bars(bars);

                Tick[] ticks = labels.Select((l, i) => new Tick(i, l)).ToArray();
                myPlot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks);
                myPlot.Axes.Bottom.TickLabelStyle.Rotation = -45;
                myPlot.Axes.Bottom.TickLabelStyle.Alignment = Alignment.MiddleRight;
                myPlot.Axes.Bottom.MinimumSize = 100;

                myPlot.Title("Precipitación Acumulada 24h (mm) — Top 15");
                myPlot.YLabel("mm");

                return await SaveAndUploadChart(myPlot, "chart_precipitacion", "📊 Gráfica de precipitación acumulada 24 horas");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating precipitation chart");
                return new BotResponse { Message = $"⚠️ Error al generar la gráfica de precipitación: {ex.Message}" };
            }
        }

        /// <summary>
        /// Generate a station sensor time series chart
        /// </summary>
        public async Task<BotResponse> GenerateStationChartAsync(string dcpId, string stationName, string? variableFilter = null, int hours = 48)
        {
            try
            {
                using var db = new NpgsqlConnection(_pgConn);
                var sql = variableFilter != null
                    ? @"SELECT variable, ts, valor FROM dcp_datos
                        WHERE dcp_id = @Id AND ts > NOW() - make_interval(hours => @Hours)
                          AND valor > -999 AND valor < 9999
                          AND LOWER(variable) LIKE @Var
                        ORDER BY variable, ts"
                    : @"SELECT variable, ts, valor FROM dcp_datos
                        WHERE dcp_id = @Id AND ts > NOW() - make_interval(hours => @Hours)
                          AND valor > -999 AND valor < 9999
                          AND variable NOT IN ('señal_de_ruido', 'temperatura_interna')
                        ORDER BY variable, ts";

                var data = (await db.QueryAsync<dynamic>(sql, new
                {
                    Id = dcpId,
                    Hours = hours,
                    Var = variableFilter != null ? $"%{variableFilter.ToLower()}%" : "%"
                })).ToList();

                if (!data.Any())
                    return new BotResponse { Message = $"⚠️ No se encontraron datos recientes de la estación {stationName} para graficar." };

                var groups = data.GroupBy(d => (string)d.variable).ToList();

                var myPlot = new ScottPlot.Plot();
                ApplyDarkTheme(myPlot);

                foreach (var g in groups)
                {
                    var timestamps = g.Select(d => ((DateTime)d.ts).ToOADate()).ToArray();
                    var values = g.Select(d => Convert.ToDouble(d.valor)).ToArray();
                    if (timestamps.Length < 2) continue;
                    var scatter = myPlot.Add.Scatter(timestamps, values);
                    scatter.LegendText = FormatVariableName(g.Key);
                    scatter.LineWidth = 2;
                    scatter.MarkerSize = 0;
                }

                myPlot.Axes.DateTimeTicksBottom();
                myPlot.Title($"Estación: {stationName}");
                myPlot.XLabel("Fecha/Hora");
                if (groups.Count == 1)
                    myPlot.YLabel(FormatVariableName(groups[0].Key));
                myPlot.ShowLegend(Alignment.UpperRight);

                return await SaveAndUploadChart(myPlot, "chart_estacion", $"📊 Gráfica de {stationName} (últimas {hours}h)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating station chart for {DcpId}", dcpId);
                return new BotResponse { Message = $"⚠️ Error al generar la gráfica de estación: {ex.Message}" };
            }
        }

        /// <summary>
        /// Generate a cuenca precipitation comparison chart
        /// </summary>
        public async Task<BotResponse> GenerateCuencaPrecipChartAsync()
        {
            try
            {
                using var db = new NpgsqlConnection(_pgConn);
                var data = (await db.QueryAsync<dynamic>(@"
                    SELECT nombre, promedio_mm, max_mm
                    FROM precipitacion_cuenca
                    WHERE ts = (SELECT MAX(ts) FROM precipitacion_cuenca)
                      AND tipo = 'cuenca' AND promedio_mm > 0
                    ORDER BY promedio_mm DESC")).ToList();

                if (!data.Any())
                    return new BotResponse { Message = "⚠️ No hay datos de precipitación por cuenca disponibles." };

                var myPlot = new ScottPlot.Plot();
                ApplyDarkTheme(myPlot);

                var labels = data.Select(d =>
                {
                    var n = (string)d.nombre;
                    return n.Length > 22 ? n[..22] + "…" : n;
                }).ToArray();

                var avgBars = data.Select((d, i) => new ScottPlot.Bar
                {
                    Position = i - 0.15,
                    Value = (double)d.promedio_mm,
                    Size = 0.28,
                    FillColor = ScottPlot.Color.FromHex("#00bcd4")
                }).ToArray();
                var maxBars = data.Select((d, i) => new ScottPlot.Bar
                {
                    Position = i + 0.15,
                    Value = (double)d.max_mm,
                    Size = 0.28,
                    FillColor = ScottPlot.Color.FromHex("#ce93d8")
                }).ToArray();

                var bp1 = myPlot.Add.Bars(avgBars);
                bp1.LegendText = "Promedio (mm)";
                var bp2 = myPlot.Add.Bars(maxBars);
                bp2.LegendText = "Máximo (mm)";

                Tick[] ticks = labels.Select((l, i) => new Tick(i, l)).ToArray();
                myPlot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks);
                myPlot.Axes.Bottom.TickLabelStyle.Rotation = -45;
                myPlot.Axes.Bottom.TickLabelStyle.Alignment = Alignment.MiddleRight;
                myPlot.Axes.Bottom.MinimumSize = 100;

                myPlot.Title("Precipitación por Cuenca");
                myPlot.YLabel("mm");
                myPlot.ShowLegend(Alignment.UpperRight);

                return await SaveAndUploadChart(myPlot, "chart_cuenca_precip", "📊 Comparación de precipitación por cuenca");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating cuenca precipitation chart");
                return new BotResponse { Message = $"⚠️ Error al generar la gráfica de cuencas: {ex.Message}" };
            }
        }

        // ============ Shared Helpers ============

        private static void ApplyDarkTheme(ScottPlot.Plot myPlot)
        {
            myPlot.FigureBackground.Color = ScottPlot.Color.FromHex("#121621");
            myPlot.DataBackground.Color = ScottPlot.Color.FromHex("#181c28");
            myPlot.Axes.Color(ScottPlot.Color.FromHex("#dcdcdc"));
            myPlot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#323746");
            myPlot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#1e2230");
            myPlot.Legend.FontColor = ScottPlot.Color.FromHex("#dcdcdc");
            myPlot.Legend.OutlineColor = ScottPlot.Color.FromHex("#444444");
        }

        private async Task<BotResponse> SaveAndUploadChart(ScottPlot.Plot myPlot, string namePrefix, string caption)
        {
            var fileName = $"{namePrefix}_{DateTime.UtcNow.Ticks}.png";

            // Save to temp file, then read bytes
            var tempDir = Path.Combine(Path.GetTempPath(), "cloudstation_charts");
            Directory.CreateDirectory(tempDir);
            var tempPath = Path.Combine(tempDir, fileName);
            myPlot.SavePng(tempPath, 800, 420);
            var imageBytes = await File.ReadAllBytesAsync(tempPath);

            // 1) Guardar en ImageStore local (prioridad)
            var chartsDir = Path.Combine(_imageStorePath, "charts");
            try
            {
                Directory.CreateDirectory(chartsDir);
                var localPath = Path.Combine(chartsDir, fileName);
                await File.WriteAllBytesAsync(localPath, imageBytes);
                try { File.Delete(tempPath); } catch { }

                var localUrl = !string.IsNullOrEmpty(_imageStoreBaseUrl)
                    ? $"{_imageStoreBaseUrl.TrimEnd('/')}/api/images/charts/{fileName}"
                    : $"/api/images/charts/{fileName}";

                return new BotResponse
                {
                    Message = caption,
                    FileUrl = localUrl,
                    FileName = fileName,
                    FileType = "image/png",
                    FileSize = imageBytes.Length
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save chart to ImageStore, using wwwroot fallback");
            }

            // Fallback: serve from wwwroot/temp
            var wwwTempDir = Path.Combine(AppContext.BaseDirectory, "wwwroot", "temp");
            Directory.CreateDirectory(wwwTempDir);
            var wwwPath = Path.Combine(wwwTempDir, fileName);
            File.Copy(tempPath, wwwPath, overwrite: true);
            try { File.Delete(tempPath); } catch { }

            return new BotResponse
            {
                Message = caption,
                FileUrl = $"/temp/{fileName}",
                FileName = fileName,
                FileType = "image/png",
                FileSize = imageBytes.Length
            };
        }

        private static string FormatVariableName(string variable)
        {
            var v = variable.Replace("_", " ");
            if (v.Length > 0) v = char.ToUpper(v[0]) + v[1..];
            return v;
        }
    }
}
