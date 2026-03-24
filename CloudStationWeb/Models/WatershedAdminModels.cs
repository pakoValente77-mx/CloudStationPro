using System;
using System.Collections.Generic;

namespace CloudStationWeb.Models
{
    // ====== CUENCA (WATERSHED) ======
    public class CuencaAdminDto
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = "";
        public string? Codigo { get; set; }
        public string? ArchivoKml { get; set; }
        public string? Color { get; set; }
        public bool Activo { get; set; }
        public bool VerEnMapa { get; set; }
        public int SubcuencaCount { get; set; }
        public int EstacionCount { get; set; }
    }

    // ====== SUBCUENCA (SUB-WATERSHED) ======
    public class SubcuencaAdminDto
    {
        public Guid Id { get; set; }
        public Guid? IdCuenca { get; set; }
        public string Nombre { get; set; } = "";
        public string? CuencaNombre { get; set; }
        public string? ArchivoKml { get; set; }
        public string? Color { get; set; }
        public bool Activo { get; set; }
        public bool VerEnMapa { get; set; }
        public int EstacionCount { get; set; }
    }

    // ====== STATION IN WATERSHED CONTEXT ======
    public class EstacionCuencaDto
    {
        public Guid Id { get; set; }
        public string? IdAsignado { get; set; }
        public string? Nombre { get; set; }
        public Guid? IdCuenca { get; set; }
        public Guid? IdSubcuenca { get; set; }
        public string? CuencaNombre { get; set; }
        public string? SubcuencaNombre { get; set; }
        public bool Activo { get; set; }
    }

    // ====== MAIN VIEW MODEL ======
    public class WatershedAdminViewModel
    {
        public List<CuencaAdminDto> Cuencas { get; set; } = new();
        public List<SubcuencaAdminDto> Subcuencas { get; set; } = new();
        public List<EstacionCuencaDto> EstacionesSinCuenca { get; set; } = new();
    }

    // ====== EDIT CUENCA VIEW MODEL ======
    public class EditCuencaViewModel
    {
        public CuencaAdminDto Cuenca { get; set; } = new();
        public List<SubcuencaAdminDto> Subcuencas { get; set; } = new();
        public List<EstacionCuencaDto> Estaciones { get; set; } = new();
    }

    // ====== REQUEST MODELS ======
    public class SaveCuencaRequest
    {
        public Guid? Id { get; set; }
        public string Nombre { get; set; } = "";
        public string? Codigo { get; set; }
        public string? Color { get; set; }
        public bool Activo { get; set; }
        public bool VerEnMapa { get; set; }
    }

    public class SaveSubcuencaRequest
    {
        public Guid? Id { get; set; }
        public Guid IdCuenca { get; set; }
        public string Nombre { get; set; } = "";
        public string? Color { get; set; }
        public bool Activo { get; set; }
        public bool VerEnMapa { get; set; }
    }

    public class AssignStationRequest
    {
        public Guid IdEstacion { get; set; }
        public Guid? IdCuenca { get; set; }
        public Guid? IdSubcuenca { get; set; }
    }

    public class BulkAssignRequest
    {
        public List<Guid> EstacionIds { get; set; } = new();
        public Guid? IdCuenca { get; set; }
        public Guid? IdSubcuenca { get; set; }
    }

    public class RemoveKmlRequest
    {
        public Guid Id { get; set; }
        public string Tipo { get; set; } = "cuenca";
    }
}
