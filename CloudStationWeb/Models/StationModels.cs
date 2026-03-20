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

    public class CuencaSemaforo
    {
        public string Code { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string Semaforo { get; set; } = "verde";
        public float PromedioMm { get; set; }
        public float MaxMm { get; set; }
        public int EstacionesConDato { get; set; }
        public int EstacionesTotal { get; set; }
    }

    public class CuencaEstacionPrecip
    {
        public string IdAsignado { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string Subcuenca { get; set; } = "";
        public float PrecipMm { get; set; }
        public bool ConDato { get; set; }
        public bool Sospechoso { get; set; }
    }

    public class PrecipCuencaRow
    {
        public string tipo { get; set; } = "";
        public string nombre { get; set; } = "";
        public float promedio_mm { get; set; }
        public float max_mm { get; set; }
        public int estaciones_con_dato { get; set; }
        public int estaciones_total { get; set; }
        public string semaforo { get; set; } = "verde";
    }

    public class CuencaKmlConfig
    {
        public string CuencaDb { get; set; } = "";
        public string Code { get; set; } = "";
        public string Label { get; set; } = "";
        public string KmlFile { get; set; } = "";
        public string Color { get; set; } = "#ff9800";
    }

    public class EventoLluviaDto
    {
        public string? IdAsignado { get; set; }
        public string? EstacionNombre { get; set; }
        public DateTime Inicio { get; set; }
        public DateTime? Fin { get; set; }
        public float AcumuladoMm { get; set; }
        public float IntensidadMaxMmh { get; set; }
        public int DuracionMinutos { get; set; }
        public string? Estado { get; set; }
        public bool Sospechoso { get; set; }
    }

    public class EventoLluviaPgRow
    {
        public string? id_asignado { get; set; }
        public string? estacion_nombre { get; set; }
        public DateTime inicio { get; set; }
        public DateTime? fin { get; set; }
        public float acumulado_mm { get; set; }
        public float intensidad_max_mmh { get; set; }
        public int duracion_minutos { get; set; }
        public string? estado { get; set; }
        public bool sospechoso { get; set; }
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
