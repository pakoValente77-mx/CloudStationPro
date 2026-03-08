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

        public DataService(IConfiguration configuration)
        {
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
                        ValorActual = validatedValue,
                        IsCfe = s.Organismo != null && (s.Organismo == "Comisión Federal de Electricidad" || s.Organismo.Contains("CFE")),
                        IsGolfoCentro = s.Organismo != null && s.Organismo.Contains("GOLFO CENTRO", StringComparison.OrdinalIgnoreCase),
                        HasCota = s.HasCota
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

                string query = @"
                    SELECT ts as Ts, valor as Valor, variable as Variable 
                    FROM public.dcp_datos 
                    WHERE dcp_id = @DcpId AND variable = @Var 
                    AND ts >= now() - interval '" + hours + @" hours'
                    ORDER BY ts ASC";

                // Si es viento, necesitamos velocidad y dirección
                if (variable.Contains("viento"))
                {
                    query = @"
                        SELECT ts as Ts, valor as Valor, variable as Variable 
                        FROM public.dcp_datos 
                        WHERE dcp_id = @DcpId 
                        AND (variable = 'velocidad_del_viento' OR variable = 'dirección_del_viento')
                        AND ts >= now() - interval '" + hours + @" hours'
                        ORDER BY ts ASC";
                }

                var history = await pgDb.QueryAsync<HistoricalMeasurement>(query, new { DcpId = dcpId, Var = variable });
                return history.ToList();
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
            
            // Convert to UTC for database query (add 6 hours)
            var startTimeUtc = startTimeCdmx.AddHours(6);
            var endTimeUtc = endTimeCdmx.AddHours(6);

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
                        // Convert UTC timestamps back to CDMX local time and format as strings
                        hourlyValues = dataByDcp[searchId].Select(d => {
                            return new HourlyValue
                            {
                                // Format as ISO string without timezone (yyyy-MM-ddTHH:mm:ss)
                                Hour = d.hour.AddHours(-6).ToString("yyyy-MM-ddTHH:mm:ss"),
                                Value = d.value,
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

        public async Task<List<string>> GetAvailableVariablesAsync()
        {
            using (IDbConnection pgDb = new NpgsqlConnection(_postgresConn))
            {
                var vars = await pgDb.QueryAsync<string>("SELECT DISTINCT variable FROM public.ultimas_mediciones ORDER BY variable");
                return vars.Where(v => v != "precipitación_acumulada").ToList();
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

                Console.WriteLine($"[DEBUG] Requested Stations: {request.StationIds.Count}, SQL Found: {basicStations.Count}");
                foreach(var s in basicStations) Console.WriteLine($"[DEBUG] Found Station: {s.IdAsignado}");

                // Get limits (Optional)
                try 
                {
                    // sqlVariable already declared outside
                    var limits = await sqlDb.QueryAsync<(string IdAsignado, double? ValorMin, double? ValorMax)>(
                        $@"SELECT e.IdAsignado, s.ValorMin, s.ValorMax 
                           FROM Estacion e
                           JOIN Sensor s ON s.IdEstacion = e.Id
                           JOIN TipoSensor t ON s.IdTipoSensor = t.Id
                           WHERE e.IdAsignado IN @StationIds
                           AND t.Nombre = @SqlVariable
                           AND s.Visible = 1",
                        new { StationIds = request.StationIds, SqlVariable = sqlVariable });
                    
                    foreach (var l in limits)
                    {
                        if (l.IdAsignado != null) limitsMap[l.IdAsignado] = (l.ValorMin, l.ValorMax);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error fetching limits: {ex.Message}");
                }

                // Map to tuple list expected by loop
                sqlStations = basicStations.Select(s => (s.IdAsignado ?? "", s.IdSatelital, s.Nombre ?? "", (double?)null, (double?)null)).ToList();
            }

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

                    series.Add(new TimeSeries
                    {
                        StationId = station.IdAsignado,
                        StationName = station.Nombre ?? "",
                        MinLimit = minLimit,
                        MaxLimit = maxLimit,
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
                "voltaje_batería" => "Voltaje de batería",
                _ => postgresVar // Fallback
            };
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
    }
}
