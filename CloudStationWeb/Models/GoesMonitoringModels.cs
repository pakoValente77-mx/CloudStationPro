using System;

namespace CloudStationWeb.Models
{
    public class TransmissionStats
    {
        public int TotalStations { get; set; }
        public int ActiveStations { get; set; }
        public int SilentStations { get; set; }
        public decimal ReceptionRate { get; set; }
        public int TotalTransmissionsLast24h { get; set; }
        public int ExpectedTransmissionsLast24h { get; set; }
    }

    public class StationHealth
    {
        public string StationId { get; set; } = string.Empty;
        public string StationName { get; set; } = string.Empty;
        public DateTime? LastTransmission { get; set; }
        public int TransmissionsLast24h { get; set; }
        public int ExpectedTransmissions { get; set; }
        public decimal HealthScore { get; set; }
        public string Status { get; set; } = "Desconocido"; // Operativa/Atención/Falla/Desconocido
        public string StatusColor { get; set; } = "#6b7280"; // Color para UI
        public int HoursSinceLastTx { get; set; }
    }

    public class TransmissionTimelinePoint
    {
        public string StationId { get; set; } = string.Empty;
        public string StationName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool Received { get; set; }
        public int TransmissionCount { get; set; }
    }

    public class TransmissionTrendPoint
    {
        public DateTime Hour { get; set; }
        public int ReceivedCount { get; set; }
        public int ExpectedCount { get; set; }
        public decimal SuccessRate { get; set; }
    }

    public class GoesMonitoringViewModel
    {
        public TransmissionStats Stats { get; set; } = new TransmissionStats();
        public List<StationHealth> StationHealthList { get; set; } = new List<StationHealth>();
        public string SelectedPeriod { get; set; } = "24h";
    }
}
