namespace CloudStationWeb.Models
{
    public class DataAnalysisRequest
    {
        public List<string> StationIds { get; set; } = new();
        public string Variable { get; set; } = "precipitación";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class DataAnalysisResponse
    {
        public string AggregationLevel { get; set; } = ""; // "raw", "hourly", "daily"
        public string Variable { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<TimeSeries> Series { get; set; } = new();
    }

    public class TimeSeries
    {
        public string StationId { get; set; } = "";
        public string StationName { get; set; } = "";
        public double? MinLimit { get; set; }
        public double? MaxLimit { get; set; }
        public bool EnMantenimiento { get; set; }
        public List<DataPoint> DataPoints { get; set; } = new();
    }

    public class DataPoint
    {
        public DateTime Timestamp { get; set; }
        public float? Value { get; set; }
        public bool IsValid { get; set; } = true;
    }

    public class StationInfo
    {
        public string Id { get; set; } = "";
        public string DatabaseId { get; set; } = "";
        public string Name { get; set; } = "";
        public double? Lat { get; set; }
        public double? Lon { get; set; }
    }

    public class StationGroup
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public string IdUsuario { get; set; } = "";
        public bool? Inicio { get; set; }
    }
}
