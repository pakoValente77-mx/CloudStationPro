using System;
using System.Collections.Generic;

namespace CloudStationWeb.Models
{
    // ====== MAIN VIEW MODEL FOR THE EDIT PAGE ======
    public class StationAdminViewModel
    {
        public EstacionDto Estacion { get; set; } = new EstacionDto();
        
        public DatosGoesDto? GoesParams { get; set; }
        public DatosGprsDto? GprsParams { get; set; }
        public DatosRadioDto? RadioParams { get; set; }

        public List<SensorAdminDto> Sensores { get; set; } = new List<SensorAdminDto>();
        public List<BitacoraDto> Bitacora { get; set; } = new List<BitacoraDto>();
        public List<UmbralDto> Umbrales { get; set; } = new List<UmbralDto>();
        
        // Catalogs for Dropdowns
        public GeograficCatalogs Catalogs { get; set; } = new GeograficCatalogs();
    }

    // ====== CORE STATION DATA ======
    public class EstacionDto
    {
        public Guid Id { get; set; } // uniqueidentifier
        public string? IdAsignado { get; set; }
        public string? Nombre { get; set; }
        public string? Coordenadas { get; set; } // Formato LAT, LNG
        public Guid? IdCuenca { get; set; }
        public Guid? IdSubcuenca { get; set; }
        public int? IdMunicipio { get; set; }
        public int? IdEntidadFederativa { get; set; }
        public bool Visible { get; set; }
        public bool Activo { get; set; }
        public bool GOES { get; set; }
        public bool GPRS { get; set; }
        public bool RADIO { get; set; }
        public double? Latitud { get; set; }
        public double? Longitud { get; set; }
        public int? IdOrganismo { get; set; }
        public string? Etiqueta { get; set; }
        public bool EsPresa { get; set; }
    }

    // ====== TELEMETRY ======
    public class DatosGoesDto
    {
        public int Id { get; set; }
        public Guid IdEstacion { get; set; }
        public string? IdSatelital { get; set; }
        public int? CanalPrimario { get; set; }
        public int? Velocidad { get; set; }
        public string? PrimeraTransmision { get; set; }
        public string? VentanaTransmision { get; set; }
        public string? Satelite { get; set; }
        public string? RangoTransmision { get; set; }
        public string? IntervaloTransmision { get; set; }
        public int? CanalAlarma { get; set; }
        public int? VelocidadAlarma { get; set; }
        public string? Decodificador { get; set; }
    }

    public class DatosGprsDto
    {
        public int Id { get; set; }
        public Guid IdEstacion { get; set; }
        public string? PeriodoTransmision { get; set; }
        public string? Comentarios { get; set; }
    }

    public class DatosRadioDto
    {
        public int Id { get; set; }
        public Guid IdEstacion { get; set; }
        public string? PeriodoTransmision { get; set; }
        public string? Comentarios { get; set; }
    }
    
    // ====== SENSOR UPDATE REQUEST ======
    public class UpdateSensorRequest
    {
        public Guid Id { get; set; }
        public string? NumeroSensor { get; set; }
        public decimal? ValorMinimo { get; set; }
        public decimal? ValorMaximo { get; set; }
        public bool Activo { get; set; }
        public int? PeriodoMuestra { get; set; }
        public string? TipoGrafica { get; set; }
        public Guid? IdUnidadMedida { get; set; }
    }

    // ====== SENSORS ======
    public class SensorAdminDto
    {
        public Guid Id { get; set; }
        public Guid IdEstacion { get; set; }
        public Guid IdTipoSensor { get; set; }
        public string? TipoSensorNombre { get; set; } // From TipoSensor
        public string? Icono { get; set; } // From TipoSensor
        public string? NumeroSensor { get; set; }
        public bool Visible { get; set; }
        public bool Activo { get; set; }
        public decimal? ValorMinimo { get; set; }
        public decimal? ValorMaximo { get; set; }
        public string? Especificacion { get; set; }
        public int? PeriodoMuestra { get; set; }
        public string? TipoGrafica { get; set; }
        
        // Medida
        public Guid? IdUnidadMedida { get; set; }
        public string? UnidadMedidaNombre { get; set; } // From UnidadMedida
        public string? UnidadMedidaAbv { get; set; } // From UnidadMedida
    }

    // ====== ACTIVITY LOGS / BITACORA ======
    public class BitacoraDto
    {
        public long Id { get; set; }
        public Guid IdEstacion { get; set; }
        public string? FechaEvento { get; set; }
        public string? FechaRegistro { get; set; }
        public string? Descripcion { get; set; }
        public string? UsuarioNombreCompleto { get; set; }
        public string? Usuario { get; set; }
        public int? IdEstatus { get; set; }
        public int? IdEstatusEstacion { get; set; }
    }

    // ====== THRESHOLDS (UMBRALES) ======
    public class UmbralDto
    {
        public long Id { get; set; }
        public Guid IdSensor { get; set; }
        public decimal? ValorReferencia { get; set; }
        public decimal? Umbral { get; set; }
        public string? Operador { get; set; }
        public string? Nombre { get; set; }
        public bool Activo { get; set; }
        public string? Color { get; set; }
        public int? Periodo { get; set; }
    }

    // ====== DROPDOWN CATALOGS ======
    public class GeograficCatalogs
    {
        public List<CatalogItemGuid> Cuencas { get; set; } = new();
        public List<CatalogItemGuid> Subcuencas { get; set; } = new();
        public List<CatalogItemInt> Entidades { get; set; } = new();
        public List<CatalogItemInt> Municipios { get; set; } = new();
        public List<CatalogItemInt> Organismos { get; set; } = new();
        public List<CatalogItemGuid> UnidadesMedida { get; set; } = new();
    }

    public class CatalogItemGuid
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }

    public class CatalogItemInt
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }
}
