using System;

namespace CloudStationWeb.Models
{
    public class StationMapData
    {
        public string? Id { get; set; }
        public string? DcpId { get; set; }
        public string? Nombre { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string? EstatusColor { get; set; } // De estatus_estaciones
        public float? ValorActual { get; set; } // De ultimas_mediciones
        public float? ValorAuxiliar { get; set; } // Para acumulados u otros datos extra
        public string? VariableActual { get; set; }
        public DateTime? UltimaTx { get; set; }
        public bool IsCfe { get; set; }
        public bool IsGolfoCentro { get; set; }
        public bool HasCota { get; set; }
    }

    public class HistoricalMeasurement
    {
        public DateTime Ts { get; set; }
        public float Valor { get; set; }
        public string? Variable { get; set; }
    }

    public class SqlServerStation
    {
        public string? IdAsignado { get; set; }
        public string? IdSatelital { get; set; }
        public string? Nombre { get; set; }
        public double Latitud { get; set; }
        public double Longitud { get; set; }
        public string? Cuenca { get; set; }
        public string? Subcuenca { get; set; }
        public string? Organismo { get; set; }
        public bool HasCota { get; set; }
    }

    public class PostgresStatus
    {
        public string? dcp_id { get; set; }
        public string? color_estatus { get; set; }
        public DateTime? fecha_ultima_tx { get; set; }
    }

    public class PostgresMeasurement
    {
        public string? dcp_id { get; set; }
        public string? variable { get; set; }
        public float valor { get; set; }
        public DateTime ts { get; set; }
    }

    public class PostgresMapping
    {
        public string? dcp_id { get; set; }
        public string? id_asignado { get; set; }
    }

    public class StationVariableAvailability
    {
        public string? Variable { get; set; } // Internal ID (e.g., "precipitación")
        public string? DisplayName { get; set; } // Human readable (e.g., "Precipitación")
        public bool HasData { get; set; }
        public DateTime? LastUpdate { get; set; }
        public Guid? SensorId { get; set; } // SQL Server Sensor Id (for CotaSensor lookup)
    }
}
