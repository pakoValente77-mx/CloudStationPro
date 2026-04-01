namespace CloudStationWeb.Models
{
    /// <summary>
    /// Record of an alert that was triggered automatically
    /// </summary>
    public class AlertRecord
    {
        public long Id { get; set; }
        public Guid IdSensor { get; set; }
        public long IdUmbral { get; set; }
        public string? NombreEstacion { get; set; }
        public string? NombreSensor { get; set; }
        public string? NombreUmbral { get; set; }
        public string? Variable { get; set; }
        public decimal ValorMedido { get; set; }
        public decimal ValorUmbral { get; set; }
        public string? Operador { get; set; }
        public string? Nivel { get; set; } // "CRITICA" | "ADVERTENCIA"
        public DateTime FechaAlerta { get; set; }
        public DateTime? FechaEnvio { get; set; }
        public int CorreosEnviados { get; set; }
        public bool Enviada { get; set; }
    }

    /// <summary>
    /// Sensor reading used for threshold evaluation
    /// </summary>
    public class SensorReading
    {
        public Guid IdSensor { get; set; }
        public Guid IdEstacion { get; set; }
        public string? NombreEstacion { get; set; }
        public string? NombreSensor { get; set; }
        public string? Variable { get; set; }
        public string? DcpId { get; set; }
        public decimal Valor { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Threshold with its associated station/sensor context
    /// </summary>
    public class UmbralConContexto
    {
        public long Id { get; set; }
        public Guid IdSensor { get; set; }
        public Guid IdEstacion { get; set; }
        public string? NombreEstacion { get; set; }
        public string? NombreSensor { get; set; }
        public string? Variable { get; set; }
        public string? DcpId { get; set; }
        public decimal? Umbral { get; set; }
        public string? Operador { get; set; }
        public string? Nombre { get; set; }
        public bool Activo { get; set; }
        public int? Periodo { get; set; } // minutes before re-alerting
    }

    public class EarlyWarningConfigDto
    {
        public bool Enabled { get; set; }
        public int IntervalSeconds { get; set; }
        public int CooldownMinutes { get; set; }
    }
}
