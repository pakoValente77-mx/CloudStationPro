namespace CloudStationWeb.Models
{
    public class HourlyReportRequest
    {
        public string Variable { get; set; } = "precipitación";
        public int StartHour { get; set; } = 6;
        public bool OnlyCfe { get; set; } = true;
    }

    public class HourlyReportResponse
    {
        public string Variable { get; set; } = "";
        public string StartTime { get; set; } = ""; // Changed to string to avoid timezone issues
        public string EndTime { get; set; } = "";   // Changed to string to avoid timezone issues
        public List<HourlyReportData> Stations { get; set; } = new();
    }

    public class HourlyReportData
    {
        public string StationId { get; set; } = "";
        public string StationName { get; set; } = "";
        public string? Cuenca { get; set; }
        public string? Subcuenca { get; set; }
        public bool HasCota { get; set; }
        public List<HourlyValue> HourlyValues { get; set; } = new();
    }

    public class HourlyValue
    {
        public string Hour { get; set; } = ""; // Changed to string to avoid timezone issues
        public float? Value { get; set; }
        public bool IsValid { get; set; } = true;
    }
}
