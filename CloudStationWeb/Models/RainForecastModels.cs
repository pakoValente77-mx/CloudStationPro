namespace CloudStationWeb.Models
{
    // ── Forecast metadata ──
    public class RainForecast
    {
        public Guid Id { get; set; }
        public DateTime ForecastDate { get; set; }
        public string ModelRun { get; set; } = "00z";
        public string? FileName { get; set; }
        public DateTime DownloadedAt { get; set; }
        public DateTime? LastUpdate { get; set; }
        public int RecordCount { get; set; }
    }

    // ── Hourly summary per subcuenca ──
    public class ForecastHourlySummary
    {
        public DateTime Ts { get; set; }
        public DateTime ForecastDate { get; set; }
        public string CuencaCode { get; set; } = "";
        public string SubcuencaName { get; set; } = "";
        public double LluviaMediaMm { get; set; }
        public double LluviaMaxMm { get; set; }
        public int NumPuntos { get; set; }
    }

    // ── Cuenca summary for the next N hours ──
    public class ForecastCuencaSummary
    {
        public string CuencaCode { get; set; } = "";
        public string CuencaLabel { get; set; } = "";
        public double LluviaAcum24h { get; set; }
        public double LluviaAcum48h { get; set; }
        public double LluviaAcum72h { get; set; }
        public double LluviaMax24h { get; set; }
        public List<ForecastHourBucket> Hourly { get; set; } = new();
    }

    public class ForecastHourBucket
    {
        public DateTime Ts { get; set; }
        public double LluviaMediaMm { get; set; }
    }

    // ── Subcuenca detail ──
    public class ForecastSubcuencaDetail
    {
        public string CuencaCode { get; set; } = "";
        public string SubcuencaName { get; set; } = "";
        public double LluviaAcum24h { get; set; }
        public double LluviaAcum48h { get; set; }
        public List<ForecastHourBucket> Hourly { get; set; } = new();
    }

    // ── Grid point for heatmap ──
    public class ForecastGridPoint
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
        public double Rain { get; set; }
    }

    // ── View models ──
    public class RainForecastIndexViewModel
    {
        public RainForecast? LatestForecast { get; set; }
        public List<ForecastCuencaSummary> CuencaSummaries { get; set; } = new();
        public List<RainForecast> RecentForecasts { get; set; } = new();
        public string ResumenMeteorologico { get; set; } = "";
    }

    // ── Condiciones atmosféricas desde API externa ──
    public class CondicionesAtmosfericas
    {
        public double Temperatura { get; set; }
        public int HumedadRelativa { get; set; }
        public double PresionAtm { get; set; }
        public double VientoVelocidad { get; set; }
        public double VientoDireccion { get; set; }
        public int CodigoTiempo { get; set; }
        public string DescripcionTiempo { get; set; } = "";
    }
}
