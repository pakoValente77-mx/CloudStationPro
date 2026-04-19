-- =====================================================================
-- deploy_embalse_config.sql
-- Tabla de configuración de embalses: NAMO, NAME, NAMINO
-- Ejecutar en IGSCLOUD (SQL Server)
-- Idempotente: se puede ejecutar múltiples veces sin error
-- =====================================================================

USE IGSCLOUD;
GO

PRINT '>> Creando tabla EmbalseConfig...'

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'EmbalseConfig')
BEGIN
    CREATE TABLE EmbalseConfig (
        Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
        PresaKey        NVARCHAR(100) NOT NULL,
        NombreDisplay   NVARCHAR(200) NOT NULL,
        Namo            DECIMAL(18,4) NULL,   -- Nivel de Aguas Máximas Ordinarias
        Name            DECIMAL(18,4) NULL,   -- Nivel de Aguas Máximas Extraordinarias
        Namino          DECIMAL(18,4) NULL,   -- Nivel de Aguas Mínimas de Operación
        IsActive        BIT NOT NULL DEFAULT 1,
        SortOrder       INT NOT NULL DEFAULT 0,
        UsuarioModifica NVARCHAR(256),
        FechaCreacion   DATETIME2 DEFAULT GETDATE(),
        FechaModifica   DATETIME2 DEFAULT GETDATE()
    );

    CREATE UNIQUE INDEX IX_EmbalseConfig_PresaKey ON EmbalseConfig(PresaKey);
    PRINT '   + Tabla EmbalseConfig creada'
END
ELSE
    PRINT '   = EmbalseConfig ya existe'
GO

-- Datos semilla: 5 embalses del sistema Grijalva
-- Tapón Juan Grijalva desactivado (no aplica NAMO/NAME/NAMINO)
IF NOT EXISTS (SELECT 1 FROM EmbalseConfig WHERE PresaKey = 'Angostura')
BEGIN
    INSERT INTO EmbalseConfig (PresaKey, NombreDisplay, Namo, Name, Namino, IsActive, SortOrder)
    VALUES ('Angostura', 'Angostura', 539.00, 542.10, 510.40, 1, 1);
    PRINT '   + Angostura insertada'
END

IF NOT EXISTS (SELECT 1 FROM EmbalseConfig WHERE PresaKey = 'Chicoasen')
BEGIN
    INSERT INTO EmbalseConfig (PresaKey, NombreDisplay, Namo, Name, Namino, IsActive, SortOrder)
    VALUES ('Chicoasen', N'Chicoasén', 395.00, 400.00, 378.50, 1, 2);
    PRINT '   + Chicoasén insertada'
END

IF NOT EXISTS (SELECT 1 FROM EmbalseConfig WHERE PresaKey = 'Malpaso')
BEGIN
    INSERT INTO EmbalseConfig (PresaKey, NombreDisplay, Namo, Name, Namino, IsActive, SortOrder)
    VALUES ('Malpaso', 'Malpaso', 189.70, 192.00, 163.00, 1, 3);
    PRINT '   + Malpaso insertada'
END

IF NOT EXISTS (SELECT 1 FROM EmbalseConfig WHERE PresaKey = 'Tapon_Juan_Grijalva')
BEGIN
    INSERT INTO EmbalseConfig (PresaKey, NombreDisplay, Namo, Name, Namino, IsActive, SortOrder)
    VALUES ('Tapon_Juan_Grijalva', N'Tapón Juan Grijalva', 100.00, 105.50, 87.00, 0, 4);
    PRINT '   + Tapón Juan Grijalva insertada (DESACTIVADA)'
END

IF NOT EXISTS (SELECT 1 FROM EmbalseConfig WHERE PresaKey = 'Penitas')
BEGIN
    INSERT INTO EmbalseConfig (PresaKey, NombreDisplay, Namo, Name, Namino, IsActive, SortOrder)
    VALUES ('Penitas', N'Peñitas', 95.10, 99.20, 84.50, 1, 5);
    PRINT '   + Peñitas insertada'
END
GO

-- Verificación
PRINT ''
PRINT '>> Verificación EmbalseConfig:'
SELECT PresaKey, NombreDisplay, Namo, Name, Namino, 
       CASE WHEN IsActive = 1 THEN 'ACTIVO' ELSE 'INACTIVO' END AS Estado
FROM EmbalseConfig ORDER BY SortOrder;
GO

PRINT '>> Deploy EmbalseConfig completado.'
GO
