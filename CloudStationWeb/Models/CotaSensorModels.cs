using System;

namespace CloudStationWeb.Models
{
    public class CotaSensor
    {
        public long Id { get; set; }
        public Guid IdSensor { get; set; }
        public decimal ValorCota { get; set; }
        public string? Operador { get; set; }
        public DateTime? FechaInicio { get; set; }
        public bool? Fin { get; set; }
        public DateTime? FechaFinal { get; set; }
        
        // Atributos de Auditoría (opcionales para el formulario frontend, 
        // pero necesarios para registrar y ver quién los dio de alta)
        public string? FechaRegistro { get; set; }
        public string? IdUsuarioRegistra { get; set; }
        public string? NombreCompleto { get; set; }
    }
}
