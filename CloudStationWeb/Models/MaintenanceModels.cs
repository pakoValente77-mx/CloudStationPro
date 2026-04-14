using System;
using System.Collections.Generic;

namespace CloudStationWeb.Models
{
    // ====== MAIN MAINTENANCE ORDER ======
    public class MantenimientoOrden
    {
        public long Id { get; set; }
        public Guid IdEstacion { get; set; }
        public string? NombreEstacion { get; set; } // JOIN
        public string? IdAsignado { get; set; } // JOIN
        public string TipoMantenimiento { get; set; } = "Correctivo";
        // Tipos: Preventivo, Correctivo, Instalación, Retiro, Calibración, Emergencia
        public string? Descripcion { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public string Estado { get; set; } = "Programado";
        // Estados: Programado, En Proceso, Completado, Cancelado
        public bool AislarDatos { get; set; } = true;
        public string? Prioridad { get; set; } = "Normal";
        // Prioridades: Baja, Normal, Alta, Urgente
        public string? ResponsableNombre { get; set; }
        public string? CreadoPor { get; set; }
        public string? CreadoPorNombre { get; set; }
        public DateTime FechaCreacion { get; set; }
        public string? ModificadoPor { get; set; }
        public string? ModificadoPorNombre { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public string? Observaciones { get; set; }

        // Navigation (for view model)
        public List<MantenimientoBitacora> Bitacoras { get; set; } = new();
        public List<MantenimientoAdjunto> Adjuntos { get; set; } = new();
        public int TotalBitacoras { get; set; }
        public int TotalAdjuntos { get; set; }
    }

    // ====== MAINTENANCE LOG ENTRY ======
    public class MantenimientoBitacora
    {
        public long Id { get; set; }
        public long IdOrden { get; set; }
        public string? Descripcion { get; set; }
        public DateTime FechaEvento { get; set; }
        public DateTime FechaRegistro { get; set; }
        public string? Usuario { get; set; }
        public string? UsuarioNombre { get; set; }
        public List<MantenimientoAdjunto> Adjuntos { get; set; } = new();
    }

    // ====== ATTACHED FILES ======
    public class MantenimientoAdjunto
    {
        public long Id { get; set; }
        public long IdOrden { get; set; }
        public long? IdBitacora { get; set; }
        public string? NombreOriginal { get; set; }
        public string? NombreAlmacenado { get; set; }
        public string? RutaArchivo { get; set; }
        public string? TipoArchivo { get; set; } // MIME type
        public long TamanoBytes { get; set; }
        public string? SubidoPor { get; set; }
        public string? SubidoPorNombre { get; set; }
        public DateTime FechaSubido { get; set; }
    }

    // ====== VIEW MODELS ======
    public class MaintenanceIndexViewModel
    {
        public List<MantenimientoOrden> Ordenes { get; set; } = new();
        public List<EstacionSimpleDto> Estaciones { get; set; } = new();
        public string? FiltroEstado { get; set; }
        public string? FiltroTipo { get; set; }
        public Guid? FiltroEstacion { get; set; }
        public bool FiltroSoloCfe { get; set; } = true;
        public int TotalActivas { get; set; }
        public int TotalEstacionesAisladas { get; set; }
    }

    public class EstacionSimpleDto
    {
        public Guid Id { get; set; }
        public string? Nombre { get; set; }
        public string? IdAsignado { get; set; }
    }

    public class MaintenanceDetailViewModel
    {
        public MantenimientoOrden Orden { get; set; } = new();
        public List<MantenimientoBitacora> Bitacoras { get; set; } = new();
        public List<MantenimientoAdjunto> AdjuntosGenerales { get; set; } = new();
    }

    // ====== REQUEST DTOs ======
    public class CreateMaintenanceRequest
    {
        public Guid IdEstacion { get; set; }
        public string TipoMantenimiento { get; set; } = "Correctivo";
        public string? Descripcion { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public bool AislarDatos { get; set; } = true;
        public string? Prioridad { get; set; } = "Normal";
        public string? ResponsableNombre { get; set; }
        public string? Observaciones { get; set; }
    }

    public class UpdateMaintenanceRequest
    {
        public long Id { get; set; }
        public string TipoMantenimiento { get; set; } = "Correctivo";
        public string? Descripcion { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public string Estado { get; set; } = "Programado";
        public bool AislarDatos { get; set; } = true;
        public string? Prioridad { get; set; } = "Normal";
        public string? ResponsableNombre { get; set; }
        public string? Observaciones { get; set; }
    }

    public class AddBitacoraRequest
    {
        public long IdOrden { get; set; }
        public string? Descripcion { get; set; }
        public DateTime FechaEvento { get; set; }
    }
}
