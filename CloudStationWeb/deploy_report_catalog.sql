-- ============================================================
-- deploy_report_catalog.sql
-- Migración: Catálogo dinámico de reportes para Centinela
-- Base de datos: IGSCLOUD (SQL Server)
-- Fecha: 2025-04-17
-- ============================================================

-- 1) Crear tabla ReportDefinitions (si no existe)
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ReportDefinitions')
BEGIN
    CREATE TABLE [ReportDefinitions] (
        [Id]           INT            IDENTITY(1,1) NOT NULL,
        [Command]      NVARCHAR(20)   NOT NULL,
        [ContentType]  NVARCHAR(50)   NOT NULL,
        [Title]        NVARCHAR(200)  NOT NULL,
        [Description]  NVARCHAR(500)  NULL,
        [Category]     NVARCHAR(50)   NOT NULL,
        [BlobName]     NVARCHAR(500)  NULL,
        [LatestPrefix] NVARCHAR(200)  NULL,
        [Caption]      NVARCHAR(500)  NULL,
        [IsActive]     BIT            NOT NULL DEFAULT 1,
        [SortOrder]    INT            NOT NULL DEFAULT 0,
        [CreatedAt]    DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt]    DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_ReportDefinitions] PRIMARY KEY ([Id])
    );

    CREATE UNIQUE INDEX [IX_ReportDefinitions_Command]  ON [ReportDefinitions] ([Command]);
    CREATE INDEX        [IX_ReportDefinitions_IsActive] ON [ReportDefinitions] ([IsActive]);

    PRINT 'Tabla ReportDefinitions creada correctamente.';
END
ELSE
BEGIN
    PRINT 'Tabla ReportDefinitions ya existe — saltando creación.';
END
GO

-- 2) Seed: insertar los 7 reportes iniciales (solo si la tabla está vacía)
IF NOT EXISTS (SELECT 1 FROM [ReportDefinitions])
BEGIN
    SET IDENTITY_INSERT [ReportDefinitions] ON;

    INSERT INTO [ReportDefinitions] ([Id],[Command],[ContentType],[Title],[Description],[Category],[BlobName],[LatestPrefix],[Caption],[IsActive],[SortOrder],[CreatedAt],[UpdatedAt])
    VALUES
    (1, '/1', 'image', 'Reporte de Unidades',           NULL, 'unidades', '9c8a7f42-3d91-4e01-a3fa-0d2e5b1c6f7d.png', NULL,                    N'📊 Reporte de Unidades actualizado.',                    1, 1, '2025-01-01','2025-01-01'),
    (2, '/2', 'image', 'Power Monitoring',               NULL, 'unidades', '6f3b2c91-91df-41b6-9a1e-c3f0d0c8e24a.png', NULL,                    N'📊 Captura del Power Monitoring.',                       1, 2, '2025-01-01','2025-01-01'),
    (3, '/3', 'image', N'Gráfica de Potencia',           NULL, 'unidades', 'b7e1f9c3-8a2d-4f5d-9c3a-7f1f6e7a2c01.png', NULL,                    N'📊 Gráfica de potencia.',                                1, 3, '2025-01-01','2025-01-01'),
    (4, '/4', 'image', N'Condición de Embalses',         NULL, 'unidades', 'e1a5f734-9c2e-4b3b-8d5a-6f7e1d2c9b8f.png', NULL,                    N'📊 Condición de embalses.',                              1, 4, '2025-01-01','2025-01-01'),
    (5, '/5', 'image', 'Aportaciones por Cuenca Propia', NULL, 'unidades', 'd42f3e19-b89c-4f02-90d4-3e7f4a6d2c01.png', NULL,                    N'📊 Aportaciones por cuenca propia.',                     1, 5, '2025-01-01','2025-01-01'),
    (6, '/6', 'image', 'Reporte de Lluvias 24h',         NULL, 'unidades', 'reporte_lluvia_1_1_638848218556433423.png', 'reporte_lluvia_1_1_',   N'📊 CFE SPH Grijalva - Reporte de lluvias 24 horas.',     1, 6, '2025-01-01','2025-01-01'),
    (7, '/7', 'image', 'Reporte Parcial de Lluvias',     NULL, 'unidades', 'reporte_lluvia_1_2_638848218556433423.png', 'reporte_lluvia_1_2_',   N'📊 CFE SPH Grijalva - Reporte parcial de lluvias.',      1, 7, '2025-01-01','2025-01-01');

    SET IDENTITY_INSERT [ReportDefinitions] OFF;

    PRINT 'Seed: 7 reportes iniciales insertados.';
END
ELSE
BEGIN
    PRINT 'Seed: La tabla ya tiene datos — saltando inserción.';
END
GO

-- 3) Registrar migración en __EFMigrationsHistory (si no se aplicó con EF)
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20260417145234_AddReportDefinitions')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES ('20260417145234_AddReportDefinitions', '8.0.11');
    PRINT 'Migración registrada en __EFMigrationsHistory.';
END
GO

-- 4) Verificación
SELECT 'ReportDefinitions' AS Tabla, COUNT(*) AS Registros FROM [ReportDefinitions];
GO

PRINT '=== Migración completada exitosamente ===';
GO
