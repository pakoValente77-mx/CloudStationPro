using Microsoft.AspNetCore.Identity;

namespace CloudStationWeb.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
        public bool IsApproved { get; set; } = true;
        public string? RegistrationNote { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public int? OrganismoId { get; set; }
        public int? CentroTrabajoId { get; set; }
        public CentroTrabajo? CentroTrabajo { get; set; }
        public bool EsTrabajadorCFE { get; set; } = true;
        public string? EmpresaExterna { get; set; }
        public string? DepartamentoExterno { get; set; }
    }

    public class CentroTrabajo
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public bool Activo { get; set; } = true;
    }
}
