-- =====================================================
-- Módulo de Mantenimiento de Estaciones
-- Script de migración para SQL Server (IGSCLOUD)
-- Fecha: 2026-03-27
-- =====================================================

-- =====================================================
-- 1. TABLA: MantenimientoOrden
--    Órdenes de mantenimiento por estación
-- =====================================================
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MantenimientoOrden')
BEGIN
    CREATE TABLE MantenimientoOrden (
        Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
        IdEstacion      UNIQUEIDENTIFIER NOT NULL,
        TipoMantenimiento NVARCHAR(50) NOT NULL DEFAULT 'Correctivo',
            -- Valores: Preventivo, Correctivo, Instalación, Retiro, Calibración, Emergencia
        Descripcion     NVARCHAR(MAX) NULL,
        FechaInicio     DATETIME2 NOT NULL,
        FechaFin        DATETIME2 NULL,
        Estado          NVARCHAR(30) NOT NULL DEFAULT 'Programado',
            -- Valores: Programado, En Proceso, Completado, Cancelado
        AislarDatos     BIT NOT NULL DEFAULT 1,
            -- Cuando = 1, los datos de la estación se excluyen de cálculos/reportes/alertas
        Prioridad       NVARCHAR(20) NULL DEFAULT 'Normal',
            -- Valores: Baja, Normal, Alta, Urgente
        ResponsableNombre NVARCHAR(200) NULL,
        Observaciones   NVARCHAR(MAX) NULL,
        CreadoPor       NVARCHAR(450) NULL,
        CreadoPorNombre NVARCHAR(200) NULL,
        FechaCreacion   DATETIME2 NOT NULL DEFAULT GETDATE(),
        ModificadoPor   NVARCHAR(450) NULL,
        ModificadoPorNombre NVARCHAR(200) NULL,
        FechaModificacion DATETIME2 NULL,

        CONSTRAINT FK_MantenimientoOrden_Estacion 
            FOREIGN KEY (IdEstacion) REFERENCES Estacion(Id)
    );

    -- Índices para consultas frecuentes
    CREATE INDEX IX_MantenimientoOrden_Estado 
        ON MantenimientoOrden(Estado) INCLUDE (IdEstacion, AislarDatos);
    
    CREATE INDEX IX_MantenimientoOrden_Estacion 
        ON MantenimientoOrden(IdEstacion) INCLUDE (Estado, AislarDatos, FechaInicio, FechaFin);
    
    CREATE INDEX IX_MantenimientoOrden_Aislamiento
        ON MantenimientoOrden(AislarDatos, Estado) 
        WHERE AislarDatos = 1 AND Estado IN ('En Proceso', 'Programado');

    PRINT 'Tabla MantenimientoOrden creada exitosamente.';
END
ELSE
    PRINT 'Tabla MantenimientoOrden ya existe, se omite creación.';
GO

-- =====================================================
-- 2. TABLA: MantenimientoBitacora
--    Entradas de bitácora por orden de mantenimiento
-- =====================================================
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MantenimientoBitacora')
BEGIN
    CREATE TABLE MantenimientoBitacora (
        Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
        IdOrden         BIGINT NOT NULL,
        Descripcion     NVARCHAR(MAX) NULL,
        FechaEvento     DATETIME2 NOT NULL,
        FechaRegistro   DATETIME2 NOT NULL DEFAULT GETDATE(),
        Usuario         NVARCHAR(450) NULL,
        UsuarioNombre   NVARCHAR(200) NULL,

        CONSTRAINT FK_MantenimientoBitacora_Orden 
            FOREIGN KEY (IdOrden) REFERENCES MantenimientoOrden(Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_MantenimientoBitacora_Orden 
        ON MantenimientoBitacora(IdOrden) INCLUDE (FechaEvento);

    PRINT 'Tabla MantenimientoBitacora creada exitosamente.';
END
ELSE
    PRINT 'Tabla MantenimientoBitacora ya existe, se omite creación.';
GO

-- =====================================================
-- 3. TABLA: MantenimientoAdjunto
--    Archivos adjuntos (fotos, videos, docs, oficios)
-- =====================================================
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MantenimientoAdjunto')
BEGIN
    CREATE TABLE MantenimientoAdjunto (
        Id               BIGINT IDENTITY(1,1) PRIMARY KEY,
        IdOrden          BIGINT NOT NULL,
        IdBitacora       BIGINT NULL,
            -- NULL = adjunto general de la orden; NOT NULL = adjunto de una entrada de bitácora
        NombreOriginal   NVARCHAR(500) NOT NULL,
        NombreAlmacenado NVARCHAR(500) NOT NULL,
        RutaArchivo      NVARCHAR(1000) NOT NULL,
        TipoArchivo      NVARCHAR(200) NULL,  -- MIME type
        TamanoBytes      BIGINT NOT NULL DEFAULT 0,
        SubidoPor        NVARCHAR(450) NULL,
        SubidoPorNombre  NVARCHAR(200) NULL,
        FechaSubido      DATETIME2 NOT NULL DEFAULT GETDATE(),

        CONSTRAINT FK_MantenimientoAdjunto_Orden 
            FOREIGN KEY (IdOrden) REFERENCES MantenimientoOrden(Id) ON DELETE CASCADE,
        CONSTRAINT FK_MantenimientoAdjunto_Bitacora 
            FOREIGN KEY (IdBitacora) REFERENCES MantenimientoBitacora(Id)
    );

    CREATE INDEX IX_MantenimientoAdjunto_Orden 
        ON MantenimientoAdjunto(IdOrden) INCLUDE (IdBitacora);

    PRINT 'Tabla MantenimientoAdjunto creada exitosamente.';
END
ELSE
    PRINT 'Tabla MantenimientoAdjunto ya existe, se omite creación.';
GO

PRINT '';
PRINT '============================================';
PRINT ' Migración de Mantenimiento completada.';
PRINT '============================================';
GO
