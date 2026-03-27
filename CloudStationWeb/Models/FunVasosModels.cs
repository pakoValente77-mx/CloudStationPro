namespace CloudStationWeb.Models
{
    public class FunVasosHorario
    {
        public DateTime Ts { get; set; }
        public string Presa { get; set; } = string.Empty;
        public short Hora { get; set; }
        public float? Elevacion { get; set; }
        public float? Almacenamiento { get; set; }
        public float? Diferencia { get; set; }
        public float? AportacionesQ { get; set; }
        public float? AportacionesV { get; set; }
        public float? ExtraccionesTurbQ { get; set; }
        public float? ExtraccionesTurbV { get; set; }
        public float? ExtraccionesVertQ { get; set; }
        public float? ExtraccionesVertV { get; set; }
        public float? ExtraccionesTotalQ { get; set; }
        public float? ExtraccionesTotalV { get; set; }
        public float? Generacion { get; set; }
        public short? NumUnidades { get; set; }
        public float? AportacionCuencaPropia { get; set; }
        public float? AportacionPromedio { get; set; }
    }

    public class FunVasosResumenPresa
    {
        public string Presa { get; set; } = string.Empty;
        public float? UltimaElevacion { get; set; }
        public float? UltimoAlmacenamiento { get; set; }
        public float? TotalAportacionesV { get; set; }
        public float? TotalExtraccionesV { get; set; }
        public float? TotalGeneracion { get; set; }
        public short UltimaHora { get; set; }
        public List<FunVasosHorario> Datos { get; set; } = new();
    }

    public class FunVasosViewModel
    {
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public List<FunVasosResumenPresa> Presas { get; set; } = new();
        public List<DateTime> FechasDisponibles { get; set; } = new();
    }
}
