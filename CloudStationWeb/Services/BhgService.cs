using Npgsql;
using Dapper;

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

        public async Task<(int presaRows, int estRows, string? error)> StoreFromUploadAsync(string tempFilePath)
        {
            // Copiar al inbox para que el parser Python lo procese
            // Alternativa: parsear directamente aquí (simplificado)
            try
            {
                var inboxDir = GetInboxDir();
                if (string.IsNullOrEmpty(inboxDir))
                    return (0, 0, "No se configuró la carpeta bhg_inbox");

                Directory.CreateDirectory(inboxDir);
                var destPath = Path.Combine(inboxDir, Path.GetFileName(tempFilePath));
                File.Copy(tempFilePath, destPath, overwrite: true);

                return (0, 0, null); // El parser Python procesará el archivo
            }
            catch (Exception ex)
            {
                return (0, 0, ex.Message);
            }
        }

        private string GetInboxDir()
        {
            // Leer desde config.ini o appsettings
            if (OperatingSystem.IsWindows())
                return @"C:\IGSCLOUD\Datos\bhg_inbox";
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "CFE/CloudStation/Datos/bhg_inbox");
        }
    }
}
