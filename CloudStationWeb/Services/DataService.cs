using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using CloudStationWeb.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace CloudStationWeb.Services
{
    public class DataService
    {
        private readonly string _sqlServerConn;
        private readonly string _postgresConn;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DataService> _logger;

        public DataService(IConfiguration configuration, ILogger<DataService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _sqlServerConn = configuration.GetConnectionString("SqlServer") ?? "";
            _postgresConn = configuration.GetConnectionString("PostgreSQL") ?? "";
        }

        public async Task<List<StationMapData>> GetMapDataAsync(string variable = "precipitación", bool onlyCfe = true)
        {
            List<SqlServerStation> sqlStations;
            List<PostgresStatus> postgresStatuses;
            List<PostgresMeasurement> postgresMeasurements;

            using (IDbConnection sqlDb = new SqlConnection(_sqlServerConn))
            {
                string sql = @"
                    SELECT 
                        e.IdAsignado, 
                        g.IdSatelital,
                        e.Nombre, 
                        e.Latitud, 
                        e.Longitud,
                        o.Nombre AS Organismo
                    FROM Estacion e
                    LEFT JOIN Organismo o ON e.IdOrganismo = o.Id
                    LEFT JOIN DatosGOES g ON g.IdEstacion = e.Id
                    WHERE e.Visible = 1 AND e.Activo = 1";
                if (onlyCfe)
                {
                    sql += " AND o.Nombre = 'Comisión Federal de Electricidad'";
                }
                sqlStations = (await sqlDb.QueryAsync<SqlServerStation>(sql)).ToList();
            }

            using (IDbConnection pgDb = new NpgsqlConnection(_postgresConn))
            {
                var mappings = (await pgDb.QueryAsync<PostgresMapping>(
                    "SELECT DISTINCT dcp_id, id_asignado FROM public.resumen_horario WHERE id_asignado IS NOT NULL")).ToList();

                postgresStatuses = (await pgDb.QueryAsync<PostgresStatus>(
                    "SELECT dcp_id, color_estatus, fecha_ultima_tx FROM public.estatus_estaciones")).ToList();

                postgresMeasurements = (await pgDb.QueryAsync<PostgresMeasurement>(
                    "SELECT dcp_id, variable, valor, ts FROM public.ultimas_mediciones WHERE variable = @Var", new { Var = variable })).ToList();

                // Mapeo id_asignado -> dcp_id
                var idMap = mappings
                    .Where(m => !string.IsNullOrEmpty(m.id_asignado))
                    .GroupBy(m => m.id_asignado!.Trim())
                    .ToDictionary(g => g.Key, g => g.First().dcp_id!.Trim());

                // Load cotas for the stations shown on the map (use measurement timespan for window)
                var sqlVariable = GetSqlVariableName(variable);
                var stationIdsForCotas = sqlStations.Select(s => s.IdAsignado).Where(id => !string.IsNullOrEmpty(id));
                DateTime windowStartUtc = DateTime.UtcNow.AddDays(-30);
                DateTime windowEndUtc = DateTime.UtcNow.AddDays(1);

                if (postgresMeasurements.Any())
                {
                    var minTs = postgresMeasurements.Min(m => m.ts);
                    var maxTs = postgresMeasurements.Max(m => m.ts);
                    windowStartUtc = minTs.AddHours(-6);
                    windowEndUtc = maxTs.AddHours(6);
                }

                var cotasByStation = await LoadCotasForStationsAsync(stationIdsForCotas, sqlVariable, windowStartUtc, windowEndUtc);

                // Load stations in active maintenance for data isolation
                var maintenanceIds = await GetStationsInMaintenanceAsync();

                // Unir datos y filtrar valores malos
                var result = sqlStations.Select(s => {
                    string searchId = !string.IsNullOrEmpty(s.IdSatelital) 
                        ? s.IdSatelital 
                        : ((s.IdAsignado != null && idMap.ContainsKey(s.IdAsignado)) 
                            ? idMap[s.IdAsignado] 
                            : (s.IdAsignado ?? ""));

                    var status = postgresStatuses.FirstOrDefault(p => p.dcp_id == searchId);
                    var measure = postgresMeasurements.FirstOrDefault(p => p.dcp_id == searchId);

                    float? validatedValue = measure?.valor;
                    var measureTs = measure?.ts;

                    if (validatedValue.HasValue)
                    {
                        if (variable.ToLower().Contains("precipitación"))
                        {
                            // Filtrar valores negativos
                            if (validatedValue < 0)
                            {
                                validatedValue = null;
                            }
                            // Filtrar valores irrazonablemente altos (>30mm en 10 min)
                            // Estos suelen ser valores acumulados reportados incorrectamente
                            else if (validatedValue > 30)
                            {
                                validatedValue = null;
                            }
                        }
                        else if (variable.ToLower().Contains("nivel") && (validatedValue < -15 || validatedValue > 1300))
                        {
                            validatedValue = null;
                        }
                    }

                    if (validatedValue.HasValue && measureTs.HasValue && !string.IsNullOrEmpty(s.IdAsignado))
                    {
                        validatedValue = ApplyCota(validatedValue.Value, measureTs.Value, s.IdAsignado, cotasByStation);
                    }

                    var isInMaint = !string.IsNullOrEmpty(s.IdAsignado) && maintenanceIds.Contains(s.IdAsignado);

                    return new StationMapData
                    {
                        Id = s.IdAsignado,
                        DcpId = searchId,
                        Nombre = s.Nombre,
                        Lat = s.Latitud,
                        Lon = s.Longitud,
                        EstatusColor = status?.color_estatus ?? "NEGRO",
                        UltimaTx = status?.fecha_ultima_tx,
                        VariableActual = variable,
                        ValorActual = isInMaint ? null : validatedValue, // Anular valor si está en mantenimiento
                        IsCfe = s.Organismo != null && (s.Organismo == "Comisión Federal de Electricidad" || s.Organismo.Contains("CFE")),
                        IsGolfoCentro = s.Organismo != null && s.Organismo.Contains("GOLFO CENTRO", StringComparison.OrdinalIgnoreCase),
                        HasCota = s.HasCota,
                        EnMantenimiento = isInMaint
                    };
                }).ToList();

                // Si la variable es precipitación, obtener acumulado de la última hora
                if (variable.ToLower().Contains("precipitación"))
                {
                    var accumulations = (await pgDb.QueryAsync<(string dcp_id, float suma)>(
                        @"SELECT dcp_id, SUM(suma) as suma 
                          FROM public.resumen_horario 
                          WHERE variable = 'precipitación' 
                          AND ts >= now() - interval '1 hour'
                          GROUP BY dcp_id")).ToDictionary(x => x.dcp_id, x => x.suma);

                    foreach (var d in result)
                    {
                        if (!string.IsNullOrEmpty(d.DcpId) && accumulations.TryGetValue(d.DcpId, out float suma))
                        {
                            d.ValorAuxiliar = suma;
                        }
                    }
                }

                return result;
            }
        }

        public async Task<object?> GetStationBannerAsync(string stationId)
        {
            string? dcpId = null;
            string nombre = stationId;

            // 1. Obtener nombre e IdSatelital desde SQL Server
            using (IDbConnection sqlDb = new SqlConnection(_sqlServerConn))
            {
                var info = await sqlDb.QueryFirstOrDefaultAsync<dynamic>(
                    @"SELECT TOP 1 e.Nombre, g.IdSatelital 
                      FROM Estacion e
                      LEFT JOIN DatosGOES g ON g.IdEstacion = e.Id
                      WHERE e.IdAsignado = @Id", new { Id = stationId });

                if (info != null)
                {
                    nombre = info.Nombre ?? stationId;
                    dcpId = info.IdSatelital;
                }
            }

            // 2. Fallback dcpId
            using (IDbConnection pgDb = new NpgsqlConnection(_postgresConn))
            {
                if (string.IsNullOrEmpty(dcpId))
                {
                    dcpId = await pgDb.QueryFirstOrDefaultAsync<string>(
                        "SELECT dcp_id FROM public.resumen_horario WHERE id_asignado = @Id LIMIT 1", new { Id = stationId });
                }
                if (string.IsNullOrEmpty(dcpId)) dcpId = stationId;

                // 3. Obtener todas las variables actuales
                var rows = (await pgDb.QueryAsync<dynamic>(
                    @"SELECT variable, valor, ts 
                      FROM public.ultimas_mediciones 
                      WHERE dcp_id = @DcpId
                      ORDER BY variable", new { DcpId = dcpId })).ToList();

                DateTime? ultimaTx = rows.Any() ? rows.Max(r => (DateTime?)r.ts) : null;

                var variables = rows.Select(r => new
                {
                    variable = (string)r.variable,
                    valor = r.valor != null ? (double?)Convert.ToDouble(r.valor) : null,
                    unidad = GetUnidad((string)r.variable)
                }).ToList();

                return new { nombre, ultimaTx, variables };
            }
        }

        private static string GetUnidad(string variable)
        {
            if (variable.Contains("precipitaci")) return "mm/h";
            if (variable.Contains("temperat")) return "°C";
            if (variable.Contains("humedad")) return "%";
            if (variable.Contains("velocidad") && variable.Contains("viento")) return "m/s";
            if (variable.Contains("direcci") && variable.Contains("viento")) return "°";
            if (variable.Contains("presi")) return "hPa";
            if (variable.Contains("radiaci")) return "W/m²";
            if (variable.Contains("batería") || variable.Contains("bateria")) return "V";
            if (variable.Contains("nivel") || variable.Contains("cota")) return "msnm";
            return variable;
        }

        public async Task<List<HistoricalMeasurement>> GetStationHistoryAsync(string stationId, string variable, int hours = 6)
        {
            string? dcpId = null;
            
            // 1. Intentar obtener IdSatelital oficial
            using (IDbConnection sqlDb = new SqlConnection(_sqlServerConn))
            {
                dcpId = await sqlDb.QueryFirstOrDefaultAsync<string>(
                    @"SELECT TOP 1 g.IdSatelital 
                      FROM DatosGOES g 
                      INNER JOIN Estacion e ON g.IdEstacion = e.Id 
                      WHERE e.IdAsignado = @Id", new { Id = stationId });
            }

            // 2. Fallback a mapeo en Postgres si no es GOES
            using (IDbConnection pgDb = new NpgsqlConnection(_postgresConn))
            {
                if (string.IsNullOrEmpty(dcpId))
                {
                    dcpId = await pgDb.QueryFirstOrDefaultAsync<string>(
                        "SELECT dcp_id FROM public.resumen_horario WHERE id_asignado = @Id LIMIT 1", new { Id = stationId });
                }
                
                if (string.IsNullOrEmpty(dcpId)) dcpId = stationId;

                // FIX CVE-C2: usar parámetro @Cutoff en lugar de concatenar 'hours' directamente
                var cutoff = DateTime.UtcNow.AddHours(-hours);

                string query = @"
                    SELECT ts as Ts, valor as Valor, variable as Variable 
                    FROM public.dcp_datos 
                    WHERE dcp_id = @DcpId AND variable = @Var 
                    AND ts >= @Cutoff
                    ORDER BY ts ASC";

                // Si es viento, necesitamos velocidad y dirección
                if (variable.Contains("viento"))
                {
                    query = @"
                        SELECT ts as Ts, valor as Valor, variable as Variable 
                        FROM public.dcp_datos 
                        WHERE dcp_id = @DcpId 
                        AND (variable = 'velocidad_del_viento' OR variable = 'dirección_del_viento')
                        AND ts >= @Cutoff
                        ORDER BY ts ASC";
                }

                var history = (await pgDb.QueryAsync<HistoricalMeasurement>(query, new { DcpId = dcpId, Var = variable, Cutoff = cutoff })).ToList();

                // Apply cotas (offsets) based on timestamp and validity ranges
                var sqlVariable = GetSqlVariableName(variable);
                var endUtc = DateTime.UtcNow;
                var startUtc = endUtc.AddHours(-hours);

                var cotasByStation = await LoadCotasForStationsAsync(new[] { stationId }, sqlVariable, startUtc, endUtc);

                foreach (var h in history)
                {
                    if (h.Ts != default)
                        h.Valor = ApplyCota(h.Valor, h.Ts, stationId, cotasByStation);
                }

                return history;
            }
        }

        public async Task<HourlyReportResponse> GetHourlyReportAsync(string variable = "precipitación", int startHour = 6, bool onlyCfe = true, DateTime? targetDate = null, int? groupId = null)
        {
            // Calculate time range in CDMX local time (UTC-6)
            var nowCdmx = DateTime.UtcNow.AddHours(-6); // Current CDMX time
            
            DateTime endDateCdmx;
            if (targetDate.HasValue)
            {
                // Use the provided target date as the END date (cutoff date)
                endDateCdmx = targetDate.Value.Date;
            }
            else
            {
                // Current period: use tomorrow as end date
                // (today 6am to tomorrow 6am)
                endDateCdmx = nowCdmx.Date.AddDays(1);
            }
            
            // Create the time range in CDMX local time
            // From (endDate - 1 day) at startHour to endDate at startHour
            var startTimeCdmx = endDateCdmx.AddDays(-1).AddHours(startHour); // Previous day at startHour
            var endTimeCdmx = endDateCdmx.AddHours(startHour); // End date at startHour
            
            // Convert to UTC for database query (add 6 hours for CDMX→UTC)
            // SpecifyKind=Utc ensures Npgsql sends as UTC regardless of DateTime source Kind
            var startTimeUtc = DateTime.SpecifyKind(startTimeCdmx.AddHours(6), DateTimeKind.Utc);
            // +1 hour extra so ts < endTimeUtc includes the last hour boundary (e.g. 06:00 of cut day)
            var endTimeUtc = DateTime.SpecifyKind(endTimeCdmx.AddHours(6 + 1), DateTimeKind.Utc);

            var sqlVariable = GetSqlVariableName(variable);
            
            List<SqlServerStation> sqlStations;
            using (IDbConnection sqlDb = new SqlConnection(_sqlServerConn))
            {
                // Add CFE filter if requested
                string sql = @"
                    SELECT 
                        e.IdAsignado, 
                        g.IdSatelital,
                        e.Nombre, 
                        c.Nombre AS Cuenca, 
                        sc.Nombre AS Subcuenca,
                        o.Nombre AS Organismo,
                        CAST(MAX(CASE WHEN s.AplicaCota = 1 THEN 1 ELSE 0 END) AS BIT) as HasCota
                    FROM Estacion e
                    LEFT JOIN Cuenca c ON e.IdCuenca = c.Id
                    LEFT JOIN Subcuenca sc ON e.IdSubcuenca = sc.Id
                    LEFT JOIN Organismo o ON e.IdOrganismo = o.Id
                    LEFT JOIN DatosGOES g ON g.IdEstacion = e.Id
                    LEFT JOIN Sensor s ON s.IdEstacion = e.Id AND s.Visible = 1
                    LEFT JOIN TipoSensor ts ON ts.Id = s.IdTipoSensor AND ts.Nombre = @SqlVar
                    WHERE e.Visible = 1 AND e.Activo = 1
                    AND EXISTS (
                        SELECT 1 FROM Sensor s2 
                        JOIN TipoSensor ts2 ON ts2.Id = s2.IdTipoSensor 
                        WHERE s2.IdEstacion = e.Id AND s2.Visible = 1 AND ts2.Nombre = @SqlVar
                    )";
                
                if (onlyCfe)
                {
                    sql += " AND o.Nombre = 'Comisión Federal de Electricidad'";
                }
                
                if (groupId.HasValue)
                {
                    sql += " AND e.Id IN (SELECT IdEstacion FROM EstacionGrupoUsuario WHERE IdGrupoUsuario = @GroupId)";
                }
                
                sql += @" GROUP BY e.IdAsignado, g.IdSatelital, e.Nombre, c.Nombre, sc.Nombre, o.Nombre
                          ORDER BY e.Nombre ASC";
                
                sqlStations = (await sqlDb.QueryAsync<SqlServerStation>(sql, new { SqlVar = sqlVariable, GroupId = groupId })).ToList();
            }

            using (IDbConnection pgDb = new NpgsqlConnection(_postgresConn))
            {
                // Get mappings
                var mappings = (await pgDb.QueryAsync<PostgresMapping>(
                    "SELECT DISTINCT dcp_id, id_asignado FROM public.resumen_horario WHERE id_asignado IS NOT NULL")).ToList();

                var idMap = mappings
                    .Where(m => !string.IsNullOrEmpty(m.id_asignado))
                    .GroupBy(m => m.id_asignado!.Trim())
                    .ToDictionary(g => g.Key, g => g.First().dcp_id!.Trim());

                bool isPrecipitation = variable.ToLower().Contains("precipitación");
                string query;

                if (isPrecipitation)
                {
                    // Lluvia: obtener el acumulado de la hora usanda resumen_horario
                    query = @"
                        SELECT 
                            dcp_id,
                            date_trunc('hour', ts) as hour,
                            SUM(suma) as value,
                            true as is_valid
                        FROM public.resumen_horario
                        WHERE variable = @Variable
                          AND ts >= @StartTime
                          AND ts < @EndTime
                        GROUP BY dcp_id, date_trunc('hour', ts)
                        ORDER BY dcp_id, hour";
                }
                else
                {
                    // Otras variables (como Nivel de Agua): valor exacto registrado al minuto 0
                    query = @"
                        SELECT DISTINCT ON (dcp_id, date_trunc('hour', ts))
                            dcp_id,
                            date_trunc('hour', ts) as hour,
                            valor as value,
                            COALESCE(valido, true) as is_valid
                        FROM public.dcp_datos
                        WHERE variable = @Variable
                          AND ts >= @StartTime
                          AND ts < @EndTime
                          AND EXTRACT(MINUTE FROM ts) = 0
                        ORDER BY dcp_id, date_trunc('hour', ts), ts ASC";
                }

                var hourlyData = await pgDb.QueryAsync<(string dcp_id, DateTime hour, float? value, bool is_valid)>(
                    query, 
                    new { Variable = variable, StartTime = startTimeUtc, EndTime = endTimeUtc });

                // Group by DCP ID
                var dataByDcp = hourlyData.GroupBy(d => d.dcp_id).ToDictionary(g => g.Key, g => g.ToList());

                // Load cotas (offsets) for the stations in the report window.
                var cotasByStation = await LoadCotasForStationsAsync(
                    sqlStations.Select(s => s.IdAsignado),
                    sqlVariable,
                    startTimeUtc,
                    endTimeUtc);

                // Load stations in active maintenance for flagging (historical range)
                var maintenanceIds = await GetStationsInMaintenanceDuringAsync(startTimeCdmx, endTimeCdmx);

                // Build response
                var stations = sqlStations.Select(s => {
                    // Si la estación tiene IdSatelital oficial (GOES), lo usamos estrictamente.
                    // Si no, recaemos en el mapa o IdAsignado para estaciones remotas celulares/IoT.
                    string searchId = !string.IsNullOrEmpty(s.IdSatelital) 
                        ? s.IdSatelital 
                        : ((s.IdAsignado != null && idMap.ContainsKey(s.IdAsignado)) 
                            ? idMap[s.IdAsignado] 
                            : (s.IdAsignado ?? ""));

                    var hourlyValues = new List<HourlyValue>();
                    
                    if (dataByDcp.ContainsKey(searchId))
                    {
                        var rawData = dataByDcp[searchId];

                        // Convert UTC timestamps back to CDMX local time and format as strings
                        hourlyValues = rawData.Select(d => {
                            float? value = d.value;

                            if (value.HasValue && !string.IsNullOrEmpty(s.IdAsignado))
                                value = ApplyCota(value.Value, d.hour, s.IdAsignado, cotasByStation);

                            return new HourlyValue
                            {
                                // Format as ISO string without timezone (yyyy-MM-ddTHH:mm:ss)
                                Hour = d.hour.AddHours(-6).ToString("yyyy-MM-ddTHH:mm:ss"),
                                Value = value,
                                IsValid = d.is_valid
                            };
                        }).ToList();
                    }

                    return new HourlyReportData
                    {
                        StationId = s.IdAsignado ?? "",
                        StationName = s.Nombre ?? "",
                        Cuenca = s.Cuenca,
                        Subcuenca = s.Subcuenca,
                        HasCota = s.HasCota,
                        EnMantenimiento = !string.IsNullOrEmpty(s.IdAsignado) && maintenanceIds.Contains(s.IdAsignado),
                        HourlyValues = hourlyValues
                    };
                }).ToList();

                return new HourlyReportResponse
                {
                    Variable = variable,
                    // Format as ISO string without timezone
                    StartTime = startTimeCdmx.ToString("yyyy-MM-ddTHH:mm:ss"),
                    EndTime = endTimeCdmx.ToString("yyyy-MM-ddTHH:mm:ss"),
                    Stations = stations
                };
            }
        }

        public async Task<List<StationGroup>> GetStationGroupsAsync(string userId)
        {
            using (IDbConnection sqlDb = new SqlConnection(_sqlServerConn))
            {
                var sql = @"
                    SELECT Id, Nombre, IdUsuario, Inicio
                    FROM GrupoUsuario
                    WHERE IdUsuario = @UserId
                    ORDER BY Nombre ASC";
                
                var groups = await sqlDb.QueryAsync<StationGroup>(sql, new { UserId = userId });
                return groups.ToList();
            }
        }

        public async Task<int> CreateStationGroupAsync(string nombre, string userId)
        {
            using (IDbConnection sqlDb = new SqlConnection(_sqlServerConn))
            {
                var sql = @"
                    INSERT INTO GrupoUsuario (IdUsuario, Nombre, Inicio)
                    OUTPUT INSERTED.Id
                    VALUES (@UserId, @Nombre, 0)";
                
                return await sqlDb.QuerySingleAsync<int>(sql, new { UserId = userId, Nombre = nombre });
            }
        }

        public async Task<bool> DeleteStationGroupAsync(int groupId, string userId)
        {
            using (IDbConnection sqlDb = new SqlConnection(_sqlServerConn))
            {
                // Verify ownership and delete in one transaction-like block
                var sql = @"
                    IF EXISTS (SELECT 1 FROM GrupoUsuario WHERE Id = @GroupId AND IdUsuario = @UserId)
                    BEGIN
                        DELETE FROM EstacionGrupoUsuario WHERE IdGrupoUsuario = @GroupId;
                        DELETE FROM GrupoUsuario WHERE Id = @GroupId AND IdUsuario = @UserId;
                        SELECT 1;
                    END
                    ELSE
                    BEGIN
                        SELECT 0;
                    END";
                
                var result = await sqlDb.QuerySingleOrDefaultAsync<int>(sql, new { GroupId = groupId, UserId = userId });
                return result == 1;
            }
        }

        public async Task<List<string>> GetGroupStationsAsync(int groupId, string userId)
        {
            using (IDbConnection sqlDb = new SqlConnection(_sqlServerConn))
            {
                // Verify ownership while fetching
                var sql = @"
                    SELECT CAST(e.IdEstacion AS VARCHAR(50))
                    FROM EstacionGrupoUsuario e
                    INNER JOIN GrupoUsuario g ON e.IdGrupoUsuario = g.Id
                    WHERE g.Id = @GroupId AND g.IdUsuario = @UserId";
                
                var stationIds = await sqlDb.QueryAsync<string>(sql, new { GroupId = groupId, UserId = userId });
                return stationIds.ToList();
            }
        }

        public async Task<bool> UpdateGroupStationsAsync(int groupId, string userId, List<string> stationIds)
        {
            using (IDbConnection sqlDb = new SqlConnection(_sqlServerConn))
            {
                // Verify ownership first
                var checkSql = "SELECT 1 FROM GrupoUsuario WHERE Id = @GroupId AND IdUsuario = @UserId";
                var owns = await sqlDb.QueryFirstOrDefaultAsync<int?>(checkSql, new { GroupId = groupId, UserId = userId });
                
                if (owns == null) return false;

                // Execute transaction
                sqlDb.Open();
                using (var transaction = sqlDb.BeginTransaction())
                {
                    try
                    {
                        // 1. Delete existing stations
                        await sqlDb.ExecuteAsync(
                            "DELETE FROM EstacionGrupoUsuario WHERE IdGrupoUsuario = @GroupId", 
                            new { GroupId = groupId }, 
                            transaction);

                        // 2. Insert new stations
                        if (stationIds != null && stationIds.Count > 0)
                        {
                            var insertSql = "INSERT INTO EstacionGrupoUsuario (IdGrupoUsuario, IdEstacion) VALUES (@GroupId, @StationId)";
                            var parameters = stationIds.Select(id => new { GroupId = groupId, StationId = Guid.Parse(id) }).ToList();
                            
                            await sqlDb.ExecuteAsync(insertSql, parameters, transaction);
                        }

                        transaction.Commit();
                        return true;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        private static readonly HashSet<string> _hiddenVariables = new(StringComparer.OrdinalIgnoreCase)
        {
            "precipitación_acumulada", "cod_tab", "estado", "estado_del_cbs", "estado_del_pluvio",
            "estado_rls", "indefinido", "virtual", "señal_de_ruido", "señal_de_velocidad",
            "señal_rls", "corriente_rls", "reflectividad", "partículas_detectadas"
        };

        private static readonly Dictionary<string, string> _variableDisplayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            { "precipitación", "Precipitación" },
            { "nivel_de_agua", "Nivel de Agua" },
            { "temperatura", "Temperatura" },
            { "humedad_relativa", "Humedad Relativa" },
            { "velocidad_del_viento", "Velocidad del Viento" },
            { "dirección_del_viento", "Dirección del Viento" },
            { "presión_atmosférica", "Presión Atmosférica" },
            { "radiación_solar", "Radiación Solar" },
            { "voltaje_de_batería", "Voltaje de Batería" },
            { "intensidad_de_precipitación", "Intensidad de Precipitación" },
            { "temperatura_interna", "Temperatura Interna" },
            { "punto_de_rocío", "Punto de Rocío" },
            { "velocidad_de_ráfaga", "Velocidad de Ráfaga" },
            { "dirección_de_ráfaga", "Dirección de Ráfaga" },
            { "nivel_recipiente", "Nivel Recipiente" },
            { "gasto", "Gasto" },
            { "voltaje", "Voltaje" },
            { "voltaje_de_bateria_radio", "Voltaje Batería Radio" },
            { "voltaje_del_panel_solar", "Voltaje Panel Solar" },
            { "velocidad_del_agua", "Velocidad del Agua" },
            { "visibilidad", "Visibilidad" },
            { "humedad_del_aire", "Humedad del Aire" }
        };

        public static string GetVariableDisplayName(string variable)
        {
            return _variableDisplayNames.TryGetValue(variable, out var name) ? name : variable;
        }

        public async Task<List<(string Value, string Label)>> GetAvailableVariablesAsync()
        {
            using (IDbConnection pgDb = new NpgsqlConnection(_postgresConn))
            {
                var vars = await pgDb.QueryAsync<string>("SELECT DISTINCT variable FROM public.ultimas_mediciones ORDER BY variable");
                return vars
                    .Where(v => !_hiddenVariables.Contains(v))
                    .Select(v => (v, GetVariableDisplayName(v)))
                    .OrderBy(x => x.Item2)
                    .ToList();
            }
        }

        public async Task<List<StationInfo>> GetStationListAsync(bool onlyCfe = true)
        {
            using (IDbConnection sqlDb = new SqlConnection(_sqlServerConn))
            {
                string sql = @"
                    SELECT e.Id AS DatabaseId, e.IdAsignado, e.Nombre, e.Latitud, e.Longitud 
                    FROM Estacion e 
                    LEFT JOIN Organismo o ON e.IdOrganismo = o.Id
                    WHERE e.Visible = 1 AND e.Activo = 1";

                if (onlyCfe)
                {
                    sql += " AND o.Nombre = 'Comisión Federal de Electricidad'";
                }
                
                sql += " ORDER BY e.Nombre";

                var stations = await sqlDb.QueryAsync<(Guid DatabaseId, string IdAsignado, string Nombre, double? Latitud, double? Longitud)>(sql);
                
                return stations.Select(s => new StationInfo
                {
                    Id = s.IdAsignado ?? "",
                    DatabaseId = s.DatabaseId.ToString(),
                    Name = s.Nombre ?? "",
                    Lat = s.Latitud,
                    Lon = s.Longitud
                }).ToList();
            }
        }

        public async Task<DataAnalysisResponse> GetDataAnalysisAsync(DataAnalysisRequest request)
        {
            // Calculate time span
            var timeSpan = request.EndDate - request.StartDate;
            string aggregationLevel;
            string sourceTable;
            string aggregationColumn;

            // Determine aggregation level based on time span
            if (timeSpan.TotalDays <= 7)
            {
                aggregationLevel = "raw";
                sourceTable = "dcp_datos";
                aggregationColumn = "valor";
            }
            else if (timeSpan.TotalDays <= 365) // 1 year
            {
                aggregationLevel = "hourly";
                sourceTable = "resumen_horario";
                aggregationColumn = request.Variable.ToLower().Contains("precipitación") ? "suma" : "maximo";
            }
            else
            {
                aggregationLevel = "daily";
                sourceTable = "resumen_diario";
                aggregationColumn = request.Variable.ToLower().Contains("precipitación") ? "suma" : "maximo";
            }

            // Get station mappings
            // Get station mappings and limits
            var sqlVariable = GetSqlVariableName(request.Variable);
            
            // 1. Get Stations using SQL connection
            List<(string IdAsignado, string? IdSatelital, string Nombre, double? ValorMin, double? ValorMax)> sqlStations;
            var limitsMap = new Dictionary<string, (double? Min, double? Max)>();

            using (IDbConnection sqlDb = new SqlConnection(_sqlServerConn))
            {
                // Dapper maneja las listas directamente pasándolas como parámetros.
                // Prevenimos SQL Inyection removiendo la concatenación.
                
                // Get basic station info (Critical)
                var basicStations = (await sqlDb.QueryAsync<SqlServerStation>(
                     @"SELECT e.IdAsignado, g.IdSatelital, e.Nombre 
                       FROM Estacion e
                       LEFT JOIN DatosGOES g ON g.IdEstacion = e.Id
                       WHERE e.IdAsignado IN @StationIds", new { StationIds = request.StationIds })).ToList();

                // FIX CVE-A3: usar ILogger en lugar de Console.WriteLine
                _logger.LogDebug("[DataService] Requested Stations: {Count}, SQL Found: {Found}",
                    request.StationIds.Count, basicStations.Count);
                foreach(var s in basicStations)
                    _logger.LogDebug("[DataService] Found Station: {Id}", s.IdAsignado);

                // Get limits (Optional)
                try 
                {
                    // sqlVariable already declared outside
                    var limits = await sqlDb.QueryAsync<(string IdAsignado, double? ValorMinimo, double? ValorMaximo)>(
                        $@"SELECT e.IdAsignado, s.ValorMinimo, s.ValorMaximo 
                           FROM Estacion e
                           JOIN Sensor s ON s.IdEstacion = e.Id
                           JOIN TipoSensor t ON s.IdTipoSensor = t.Id
                           WHERE e.IdAsignado IN @StationIds
                           AND t.Nombre = @SqlVariable
                           AND s.Visible = 1",
                        new { StationIds = request.StationIds, SqlVariable = sqlVariable });
                    
                    foreach (var l in limits)
                    {
                        if (l.IdAsignado != null) limitsMap[l.IdAsignado] = (l.ValorMinimo, l.ValorMaximo);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error fetching limits: {ex.Message}");
                }

                // Map to tuple list expected by loop
                sqlStations = basicStations.Select(s => (s.IdAsignado ?? "", s.IdSatelital, s.Nombre ?? "", (double?)null, (double?)null)).ToList();
            }

            // Preload cotas for the requested range (if any)
            var cotasByStation = await LoadCotasForStationsAsync(request.StationIds, sqlVariable, request.StartDate, request.EndDate);

            // Load stations in maintenance for data isolation (historical range)
            var maintenanceIds = await GetStationsInMaintenanceDuringAsync(request.StartDate, request.EndDate);

            using (IDbConnection pgDb = new NpgsqlConnection(_postgresConn))
            {
                // ... postgres logic ...
                var mappings = (await pgDb.QueryAsync<PostgresMapping>(
                    "SELECT DISTINCT dcp_id, id_asignado FROM public.resumen_horario WHERE id_asignado IS NOT NULL")).ToList();

                var idMap = mappings
                    .Where(m => !string.IsNullOrEmpty(m.id_asignado))
                    .GroupBy(m => m.id_asignado!.Trim())
                    .ToDictionary(g => g.Key, g => g.First().dcp_id!.Trim());

                var series = new List<TimeSeries>();

                foreach (var station in sqlStations)
                {
                    string searchId = !string.IsNullOrEmpty(station.IdSatelital)
                        ? station.IdSatelital
                        : ((station.IdAsignado != null && idMap.ContainsKey(station.IdAsignado))
                            ? idMap[station.IdAsignado]
                            : null);

                    if (string.IsNullOrEmpty(searchId))
                        continue;
                    
                    // ... data fetching ...
                    string dcpId = searchId;
                    List<DataPoint> dataPoints;

                    if (aggregationLevel == "raw")
                    {
                         var rawData = await pgDb.QueryAsync<(DateTime ts, float? valor, bool? valido)>(
                            $@"SELECT ts, valor, valido 
                               FROM public.{sourceTable} 
                               WHERE dcp_id = @DcpId 
                               AND variable = @Variable 
                               AND ts >= @StartDate 
                               AND ts < @EndDate 
                               ORDER BY ts",
                            new { DcpId = dcpId, Variable = request.Variable, StartDate = request.StartDate, EndDate = request.EndDate });

                        dataPoints = rawData.Select(d => new DataPoint { Timestamp = d.ts, Value = d.valor, IsValid = d.valido ?? true }).ToList();
                    }
                    else
                    {
                        var aggData = await pgDb.QueryAsync<(DateTime ts, float? value)>(
                            $@"SELECT ts, {aggregationColumn} as value 
                               FROM public.{sourceTable} 
                               WHERE dcp_id = @DcpId 
                               AND variable = @Variable 
                               AND ts >= @StartDate 
                               AND ts < @EndDate 
                               ORDER BY ts",
                            new { DcpId = dcpId, Variable = request.Variable, StartDate = request.StartDate, EndDate = request.EndDate });

                        dataPoints = aggData.Select(d => new DataPoint { Timestamp = d.ts, Value = d.value }).ToList();
                    }

                    // Apply cota adjustment per timestamp (if any)
                    if (!string.IsNullOrEmpty(station.IdAsignado))
                    {
                        foreach (var dp in dataPoints)
                        {
                            if (dp.Value.HasValue)
                                dp.Value = ApplyCota(dp.Value.Value, dp.Timestamp, station.IdAsignado, cotasByStation);
                        }
                    }

                    // Apply limits from map
                    double? minLimit = null;
                    double? maxLimit = null;
                    if (limitsMap.ContainsKey(station.IdAsignado))
                    {
                         minLimit = limitsMap[station.IdAsignado].Min;
                         maxLimit = limitsMap[station.IdAsignado].Max;
                    }

                    // --- TRATAMIENTO GENERAL DE OUTLIERS ---
                    bool isRain = request.Variable.ToLower().Contains("precipitación");
                    var validValues = dataPoints.Where(dp => dp.Value.HasValue).Select(dp => dp.Value.Value).OrderBy(v => v).ToList();

                    if (validValues.Count > 10 && !isRain)
                    {
                        float q1 = validValues[(int)(validValues.Count * 0.25)];
                        float q3 = validValues[(int)(validValues.Count * 0.75)];
                        float iqr = q3 - q1;
                        float median = validValues[(int)(validValues.Count * 0.5)];

                        // Definir una tolerancia dinámica según el tipo de variable
                        float allowedDelta;
                        string varName = request.Variable.ToLower();
                        
                        if (varName.Contains("nivel"))
                        {
                            // Los niveles de agua cambian gradualmente. IQR suele capturar la temporada, 
                            // pero si el agua está estancada (IQR=0), un error de sensor > 5m debe ser atrapado.
                            allowedDelta = Math.Max(iqr * 5.0f, 5.0f);
                        }
                        else if (varName.Contains("temperatura"))
                        {
                            // La temperatura cambia hasta 20 grados al día.
                            allowedDelta = Math.Max(iqr * 3.0f, 15.0f);
                        }
                        else 
                        {
                            // Viento y otros
                            allowedDelta = Math.Max(iqr * 5.0f, 30.0f);
                        }

                        float statMin = median - allowedDelta;
                        float statMax = median + allowedDelta;

                        // Límites finales (si la DB los prohíbe, se aprietan los límites)
                        float finalMin = minLimit.HasValue ? Math.Max(statMin, (float)minLimit.Value) : statMin;
                        float finalMax = maxLimit.HasValue ? Math.Min(statMax, (float)maxLimit.Value) : statMax;

                        foreach (var dp in dataPoints)
                        {
                            if (dp.Value.HasValue && dp.IsValid)
                            {
                                if (dp.Value.Value < finalMin || dp.Value.Value > finalMax)
                                {
                                    dp.IsValid = false;
                                }
                            }
                        }
                    }
                    else if (isRain)
                    {
                        foreach (var dp in dataPoints)
                        {
                            if (dp.Value.HasValue && dp.IsValid)
                            {
                                // La lluvia negativa o un acumulado absurdamente grande (>2000mm) es inválida
                                if (dp.Value.Value < 0 || dp.Value.Value > 2000)
                                {
                                    dp.IsValid = false;
                                }
                            }
                        }
                    }

                    // Check if station is in maintenance
                    bool isInMaint = maintenanceIds.Contains(station.IdAsignado);
                    if (isInMaint)
                    {
                        foreach (var dp in dataPoints)
                            dp.IsValid = false;
                    }

                    series.Add(new TimeSeries
                    {
                        StationId = station.IdAsignado,
                        StationName = station.Nombre ?? "",
                        MinLimit = minLimit,
                        MaxLimit = maxLimit,
                        EnMantenimiento = isInMaint,
                        DataPoints = dataPoints
                    });
                }

                return new DataAnalysisResponse
                {
                    AggregationLevel = aggregationLevel,
                    Variable = request.Variable,
                    StartDate = request.StartDate,
                    EndDate = request.EndDate,
                    Series = series
                };
            }
        }

        private string GetSqlVariableName(string postgresVar)
        {
            // Simple reverse mapping based on known values
            return postgresVar.ToLower() switch
            {
                "precipitación" => "Precipitación",
                "temperatura" => "Temperatura",
                "nivel_de_agua" => "Nivel de agua",
                "velocidad_del_viento" => "Velocidad del viento",
                "dirección_del_viento" => "Dirección del viento",
                "humedad_relativa" => "Humedad relativa",
                "presión_atmosférica" => "Presión Atmosférica",
                "radiación_solar" => "Radiación Solar",
                "voltaje_de_batería" => "Voltaje de batería",
                _ => postgresVar // Fallback
            };
        }

        private class CotaEntry
        {
            public DateTime? Start { get; set; }
            public DateTime? End { get; set; }
            public float Valor { get; set; }
            public string Operador { get; set; } = "+";
        }

        private async Task<Dictionary<string, List<CotaEntry>>> LoadCotasForStationsAsync(IEnumerable<string?> stationIds, string sqlVariable, DateTime windowStartUtc, DateTime windowEndUtc)
        {
            var result = new Dictionary<string, List<CotaEntry>>();

            if (stationIds == null) return result;
            var ids = stationIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            if (ids.Count == 0 || string.IsNullOrEmpty(sqlVariable)) return result;

            try
            {
                using (IDbConnection sqlDb = new SqlConnection(_sqlServerConn))
                {
                    var cotas = await sqlDb.QueryAsync<(string StationId, decimal ValorCota, string Operador, DateTime? FechaInicio, DateTime? FechaFinal, string FechaRegistro)>(
                        @"SELECT e.IdAsignado AS StationId,
                                 cs.ValorCota,
                                 cs.Operador,
                                 cs.FechaInicio,
                                 cs.FechaFinal,
                                 cs.FechaRegistro
                           FROM Estacion e
                           JOIN Sensor s ON s.IdEstacion = e.Id
                           JOIN TipoSensor ts ON ts.Id = s.IdTipoSensor
                           JOIN CotaSensor cs ON cs.IdSensor = s.Id
                           WHERE e.IdAsignado IN @StationIds
                             AND ts.Nombre = @SqlVar
                             AND s.Visible = 1
                             AND (cs.FechaFinal IS NULL OR cs.FechaFinal >= @WindowStart)
                             AND (cs.FechaInicio IS NULL OR cs.FechaInicio <= @WindowEnd)
                           ORDER BY e.IdAsignado, cs.FechaInicio DESC",
                        new { StationIds = ids, SqlVar = sqlVariable, WindowStart = windowStartUtc, WindowEnd = windowEndUtc });

                    foreach (var c in cotas)
                    {
                        if (string.IsNullOrEmpty(c.StationId)) continue;

                        if (!result.ContainsKey(c.StationId))
                            result[c.StationId] = new List<CotaEntry>();

                        result[c.StationId].Add(new CotaEntry
                        {
                            Start = c.FechaInicio,
                            End = c.FechaFinal,
                            Valor = (float)c.ValorCota,
                            Operador = string.IsNullOrEmpty(c.Operador) ? "+" : c.Operador
                        });
                        Console.WriteLine($"[COTA] Loaded: Station={c.StationId} Valor={c.ValorCota} Op={c.Operador} Start={c.FechaInicio?.ToString("u") ?? "NULL"} End={c.FechaFinal?.ToString("u") ?? "NULL"}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[COTA] ERROR loading cotas: {ex.Message}");
            }

            return result;
        }

        private float ApplyCota(float rawValue, DateTime timestampUtc, string stationId, Dictionary<string, List<CotaEntry>> cotasByStation)
        {
            if (string.IsNullOrEmpty(stationId) || cotasByStation == null || !cotasByStation.TryGetValue(stationId, out var cotas))
                return rawValue;

            var matching = cotas
                .Where(c => (!c.Start.HasValue || timestampUtc >= c.Start.Value)
                         && (!c.End.HasValue || timestampUtc <= c.End.Value))
                .OrderByDescending(c => c.Start)
                .FirstOrDefault();

            if (matching == null)
                return rawValue;

            var adjustment = matching.Valor;
            if (!string.IsNullOrEmpty(matching.Operador) && matching.Operador.Trim().StartsWith("-"))
                adjustment = -adjustment;

            return rawValue + adjustment;
        }

        public async Task<object> GetTableSchemaAsync()
        {
            using (IDbConnection sqlDb = new SqlConnection(_sqlServerConn))
            {
                var gruposUsuario = await sqlDb.QueryAsync<dynamic>(@"
                    SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME LIKE '%Grupo%'
                ");
                return new
                {
                    Grupos = gruposUsuario.ToList()
                };
            }
        }




        public async Task<List<StationVariableAvailability>> GetStationVariablesAsync(string stationId)
        {
            // Get Configured Variables from SQL Server including SensorId
            var sensors = new List<(string TipoNombre, Guid SensorId)>();
            using (IDbConnection sqlDb = new SqlConnection(_sqlServerConn))
            {
                var internalId = await sqlDb.QueryFirstOrDefaultAsync<Guid?>(
                    "SELECT Id FROM Estacion WHERE IdAsignado = @StationId", new { StationId = stationId });

                if (internalId.HasValue)
                {
                    var rows = await sqlDb.QueryAsync<(string Nombre, Guid Id)>(
                        @"SELECT t.Nombre, s.Id
                          FROM Sensor s 
                          JOIN TipoSensor t ON s.IdTipoSensor = t.Id 
                          WHERE s.IdEstacion = @InternalId AND s.Visible = 1", 
                        new { InternalId = internalId.Value });
                    sensors = rows.ToList();
                }
            }

            var nameMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Precipitación", "precipitación" },
                { "Temperatura", "temperatura" },
                { "Nivel de agua", "nivel_de_agua" },
                { "Velocidad del viento", "velocidad_del_viento" },
                { "Dirección del viento", "dirección_del_viento" },
                { "Humedad relativa", "humedad_relativa" },
                { "Presión Atmosférica", "presión_atmosférica" },
                { "Radiación Solar", "radiación_solar" },
                { "Voltaje de batería", "voltaje_batería" }
            };

            var result = new List<StationVariableAvailability>();
            foreach (var s in sensors)
            {
                string internalVar = nameMapping.ContainsKey(s.TipoNombre) ? nameMapping[s.TipoNombre] : s.TipoNombre;
                result.Add(new StationVariableAvailability
                {
                    Variable = internalVar,
                    DisplayName = s.TipoNombre,
                    HasData = true,
                    LastUpdate = DateTime.Now,
                    SensorId = s.SensorId
                });
            }

            return result;
        }

        /// <summary>
        /// Returns CFE (or all) station IDs with their cuenca/subcuenca from SQL Server.
        /// </summary>
        private async Task<List<(string IdAsignado, string Cuenca, string Subcuenca)>> GetStationCuencaMappingAsync(bool onlyCfe)
        {
            using IDbConnection sqlDb = new SqlConnection(_sqlServerConn);
            string sql = @"
                SELECT e.IdAsignado,
                       ISNULL(c.Nombre, '') AS Cuenca,
                       ISNULL(sc.Nombre, '') AS Subcuenca
                FROM Estacion e
                LEFT JOIN Cuenca c ON e.IdCuenca = c.Id
                LEFT JOIN Subcuenca sc ON e.IdSubcuenca = sc.Id
                LEFT JOIN Organismo o ON e.IdOrganismo = o.Id
                WHERE e.Activo = 1";
            if (onlyCfe)
                sql += " AND o.Nombre = 'Comisión Federal de Electricidad'";

            var rows = await sqlDb.QueryAsync(sql);
            return rows
                .Where(r => r.IdAsignado != null)
                .Select(r => (
                    IdAsignado: ((string)r.IdAsignado).Trim(),
                    Cuenca: ((string)(r.Cuenca ?? "")).Trim(),
                    Subcuenca: ((string)(r.Subcuenca ?? "")).Trim()
                ))
                .ToList();
        }

        public async Task<List<CuencaSemaforo>> GetCuencaSemaforoAsync(bool onlyCfe = true)
        {
            // 1) Get station → cuenca mapping from SQL Server (respects onlyCfe filter)
            var stationMapping = await GetStationCuencaMappingAsync(onlyCfe);
            var stationIds = stationMapping.Select(s => s.IdAsignado).ToArray();

            // 1b) Exclude stations in active maintenance with data isolation
            var maintenanceIds = await GetStationsInMaintenanceAsync();
            stationMapping = stationMapping.Where(s => !maintenanceIds.Contains(s.IdAsignado)).ToList();

            // 2) Read cuenca code from DB Cuenca table
            using IDbConnection sqlDb = new SqlConnection(_sqlServerConn);
            var cuencaRows = await sqlDb.QueryAsync("SELECT Nombre, Codigo FROM Cuenca WHERE Activo = 1");
            var cuencaCodeByName = cuencaRows
                .GroupBy(r => ((string)r.Nombre).Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => ((string)(g.First().Codigo ?? "")).Trim(),
                    StringComparer.OrdinalIgnoreCase);

            // 3) Get last hour precipitation per station from PostgreSQL
            using IDbConnection pgDb = new NpgsqlConnection(_postgresConn);
            var nowUtc = DateTime.UtcNow;
            var tsHora = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc);
            var desde = tsHora.AddHours(-1);

            var precipRows = (await pgDb.QueryAsync<(string id_asignado, float precip_mm)>(
                @"SELECT id_asignado, COALESCE(SUM(acumulado), 0)::real AS precip_mm
                  FROM resumen_horario
                  WHERE variable = 'precipitación'
                    AND ts >= @Desde AND ts < @Hasta
                    AND id_asignado = ANY(@Ids)
                  GROUP BY id_asignado",
                new { Desde = desde, Hasta = tsHora, Ids = stationIds })).ToList();

            var precipByStation = precipRows.ToDictionary(r => r.id_asignado, r => r.precip_mm);

            // 3b) Get station IDs with active suspicious events to exclude from average
            var suspiciousIds = (await pgDb.QueryAsync<string>(
                @"SELECT DISTINCT id_asignado FROM eventos_lluvia
                  WHERE sospechoso = true
                    AND id_asignado = ANY(@Ids)
                    AND inicio <= @Hasta
                    AND (fin IS NULL OR fin >= @Desde)",
                new { Ids = stationIds, Desde = desde, Hasta = tsHora })).ToHashSet();

            // 4) Group by cuenca name from DB
            var cuencaGroups = stationMapping
                .Where(s => !string.IsNullOrWhiteSpace(s.Cuenca) &&
                            !s.Cuenca.Equals("Indefinida", StringComparison.OrdinalIgnoreCase))
                .GroupBy(s => s.Cuenca, StringComparer.OrdinalIgnoreCase);

            var result = new List<CuencaSemaforo>();
            foreach (var group in cuencaGroups)
            {
                string cuencaName = group.Key;
                int totalEst = group.Count();
                var valores = new List<float>();
                foreach (var s in group)
                {
                    if (suspiciousIds.Contains(s.IdAsignado)) continue;
                    if (precipByStation.TryGetValue(s.IdAsignado, out var pv))
                        valores.Add(pv);
                }

                float promedio = valores.Count > 0 ? valores.Average() : 0;
                float maxMm = valores.Count > 0 ? valores.Max() : 0;
                string sem = promedio < 2.5f ? "verde"
                           : promedio < 7.5f ? "amarillo"
                           : promedio < 15f  ? "naranja"
                           : "rojo";

                string code = cuencaCodeByName.TryGetValue(cuencaName, out var dbCode) && !string.IsNullOrEmpty(dbCode)
                    ? dbCode : cuencaName;

                result.Add(new CuencaSemaforo
                {
                    Code = code, Nombre = cuencaName, Tipo = "cuenca", Semaforo = sem,
                    PromedioMm = promedio, MaxMm = maxMm,
                    EstacionesConDato = valores.Count, EstacionesTotal = totalEst
                });
            }

            return result;
        }

        public async Task<List<EventoLluviaDto>> GetEventosLluvia24hAsync(bool onlyCfe = true)
        {
            // Get station IDs to filter by
            string[] stationIds = Array.Empty<string>();
            if (onlyCfe)
            {
                using IDbConnection sqlDb = new SqlConnection(_sqlServerConn);
                var ids = await sqlDb.QueryAsync<string>(
                    @"SELECT e.IdAsignado FROM Estacion e
                      LEFT JOIN Organismo o ON e.IdOrganismo = o.Id
                      WHERE e.Activo = 1 AND o.Nombre = 'Comisión Federal de Electricidad'");
                stationIds = ids.Where(id => !string.IsNullOrEmpty(id)).Select(id => id.Trim()).ToArray();
            }

            // Exclude stations in active maintenance with data isolation
            var maintenanceIds = await GetStationsInMaintenanceAsync();

            using IDbConnection pgDb = new NpgsqlConnection(_postgresConn);

            string sql = @"SELECT id_asignado, estacion_nombre, inicio, fin, acumulado_mm, intensidad_max_mmh,
                                  duracion_minutos, estado, sospechoso
                           FROM eventos_lluvia
                           WHERE inicio >= NOW() - INTERVAL '24 hours'
                             AND sospechoso = false";
            if (onlyCfe)
                sql += " AND id_asignado = ANY(@Ids)";
            sql += " ORDER BY inicio DESC LIMIT 50";

            var rows = (await pgDb.QueryAsync<EventoLluviaPgRow>(sql,
                onlyCfe ? new { Ids = stationIds } : null)).ToList();

            return rows.Select(r => new EventoLluviaDto
            {
                IdAsignado = r.id_asignado,
                EstacionNombre = r.estacion_nombre,
                Inicio = r.inicio,
                Fin = r.fin,
                AcumuladoMm = r.acumulado_mm,
                IntensidadMaxMmh = r.intensidad_max_mmh,
                DuracionMinutos = r.duracion_minutos,
                Estado = r.estado,
                Sospechoso = r.sospechoso
            })
            .Where(e => !maintenanceIds.Contains(e.IdAsignado ?? ""))
            .ToList();
        }

        public async Task<List<HistoricalMeasurement>> GetStationHyetographAsync(string stationId, int hours = 24)
        {
            using IDbConnection pgDb = new NpgsqlConnection(_postgresConn);

            // Get hourly accumulated precipitation from resumen_horario
            var data = (await pgDb.QueryAsync<HistoricalMeasurement>(
                @"SELECT ts AS ""Ts"", COALESCE(acumulado, 0) AS ""Valor"", 'precipitación' AS ""Variable""
                  FROM resumen_horario
                  WHERE id_asignado = @Id
                    AND variable = 'precipitación'
                    AND ts >= NOW() - make_interval(hours => @Hours)
                  ORDER BY ts ASC",
                new { Id = stationId, Hours = hours })).ToList();

            return data;
        }

        public async Task<List<CuencaEstacionPrecip>> GetCuencaEstacionesAsync(string cuencaName, bool onlyCfe = true)
        {
            // Resolve cuenca DB name: if a KML code was passed, look up in Cuenca table first, then fallback to config
            using IDbConnection sqlDb = new SqlConnection(_sqlServerConn);
            string dbCuencaName = cuencaName;

            // Try resolving code from DB
            var dbCuenca = await sqlDb.QueryFirstOrDefaultAsync<string>(
                "SELECT Nombre FROM Cuenca WHERE Codigo = @Code AND Activo = 1",
                new { Code = cuencaName });
            if (!string.IsNullOrEmpty(dbCuenca))
            {
                dbCuencaName = dbCuenca;
            }
            else
            {
                // Fallback: appsettings config
                var kmlConfig = _configuration.GetSection("CuencasKml").Get<List<CuencaKmlConfig>>() ?? new();
                var byCode = kmlConfig.FirstOrDefault(c => c.Code.Equals(cuencaName, StringComparison.OrdinalIgnoreCase));
                if (byCode != null) dbCuencaName = byCode.CuencaDb;
            }

            // Get stations from SQL Server filtered by cuenca
            string sql = @"
                SELECT e.IdAsignado, e.Nombre,
                       ISNULL(c.Nombre, '') AS Cuenca,
                       ISNULL(sc.Nombre, '') AS Subcuenca
                FROM Estacion e
                LEFT JOIN Cuenca c ON e.IdCuenca = c.Id
                LEFT JOIN Subcuenca sc ON e.IdSubcuenca = sc.Id
                LEFT JOIN Organismo o ON e.IdOrganismo = o.Id
                WHERE e.Activo = 1 AND c.Nombre = @CuencaName";
            if (onlyCfe)
                sql += " AND o.Nombre = 'Comisión Federal de Electricidad'";

            var matchedStations = (await sqlDb.QueryAsync(sql, new { CuencaName = dbCuencaName }))
                .Where(s => s.IdAsignado != null)
                .Select(s => new {
                    IdAsignado = ((string)s.IdAsignado).Trim(),
                    Nombre = ((string)(s.Nombre ?? "")).Trim(),
                    Subcuenca = ((string)(s.Subcuenca ?? "")).Trim()
                }).ToList();

            if (matchedStations.Count == 0)
                return new List<CuencaEstacionPrecip>();

            // Get last hour precipitation
            var ids = matchedStations.Select(s => s.IdAsignado).ToArray();
            using IDbConnection pgDb = new NpgsqlConnection(_postgresConn);
            var nowUtc = DateTime.UtcNow;
            var tsHora = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc);
            var desde = tsHora.AddHours(-1);

            var precipRows = (await pgDb.QueryAsync<(string id_asignado, float precip_mm)>(
                @"SELECT id_asignado, COALESCE(SUM(acumulado), 0)::real AS precip_mm
                  FROM resumen_horario
                  WHERE variable = 'precipitación'
                    AND ts >= @Desde AND ts < @Hasta
                    AND id_asignado = ANY(@Ids)
                  GROUP BY id_asignado",
                new { Desde = desde, Hasta = tsHora, Ids = ids })).ToDictionary(r => r.id_asignado, r => r.precip_mm);

            // Flag stations with active suspicious events
            var suspiciousIds = (await pgDb.QueryAsync<string>(
                @"SELECT DISTINCT id_asignado FROM eventos_lluvia
                  WHERE sospechoso = true
                    AND id_asignado = ANY(@Ids)
                    AND inicio <= @Hasta
                    AND (fin IS NULL OR fin >= @Desde)",
                new { Ids = ids, Desde = desde, Hasta = tsHora })).ToHashSet();

            return matchedStations.Select(s => new CuencaEstacionPrecip
            {
                IdAsignado = s.IdAsignado,
                Nombre = s.Nombre,
                Subcuenca = s.Subcuenca,
                PrecipMm = precipRows.TryGetValue(s.IdAsignado, out var v) ? v : 0,
                ConDato = precipRows.ContainsKey(s.IdAsignado),
                Sospechoso = suspiciousIds.Contains(s.IdAsignado)
            }).OrderByDescending(s => s.PrecipMm).ToList();
        }

        /// <summary>
        /// Returns the set of IdAsignado for stations currently in active maintenance with data isolation.
        /// </summary>
        public async Task<HashSet<string>> GetStationsInMaintenanceAsync()
        {
            using (IDbConnection sqlDb = new SqlConnection(_sqlServerConn))
            {
                var ids = await sqlDb.QueryAsync<string>(@"
                    SELECT DISTINCT e.IdAsignado
                    FROM MantenimientoOrden o
                    INNER JOIN Estacion e ON o.IdEstacion = e.Id
                    WHERE o.AislarDatos = 1 
                    AND o.Estado IN ('En Proceso', 'Programado')
                    AND o.FechaInicio <= GETDATE()
                    AND (o.FechaFin IS NULL OR o.FechaFin >= GETDATE())
                    AND e.IdAsignado IS NOT NULL");
                return ids.Where(id => !string.IsNullOrEmpty(id)).ToHashSet();
            }
        }

        /// <summary>
        /// Returns the set of IdAsignado for stations that had maintenance with data isolation
        /// overlapping the specified date range (includes completed maintenance).
        /// </summary>
        public async Task<HashSet<string>> GetStationsInMaintenanceDuringAsync(DateTime rangeStart, DateTime rangeEnd)
        {
            using (IDbConnection sqlDb = new SqlConnection(_sqlServerConn))
            {
                var ids = await sqlDb.QueryAsync<string>(@"
                    SELECT DISTINCT e.IdAsignado
                    FROM MantenimientoOrden o
                    INNER JOIN Estacion e ON o.IdEstacion = e.Id
                    WHERE o.AislarDatos = 1 
                    AND o.Estado IN ('En Proceso', 'Programado', 'Completado')
                    AND o.FechaInicio <= @RangeEnd
                    AND (o.FechaFin IS NULL OR o.FechaFin >= @RangeStart)
                    AND e.IdAsignado IS NOT NULL",
                    new { RangeStart = rangeStart, RangeEnd = rangeEnd });
                return ids.Where(id => !string.IsNullOrEmpty(id)).ToHashSet();
            }
        }
    }
}
