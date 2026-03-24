-- =============================================
-- CloudStation: Agregar columnas KML a Cuenca y Subcuenca
-- Ejecutar en la base de datos IGSCLOUD (SQL Server)
-- =============================================

-- Cuenca: Agregar Codigo, ArchivoKml, Color
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Cuenca') AND name = 'Codigo')
    ALTER TABLE Cuenca ADD Codigo NVARCHAR(20) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Cuenca') AND name = 'ArchivoKml')
    ALTER TABLE Cuenca ADD ArchivoKml NVARCHAR(255) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Cuenca') AND name = 'Color')
    ALTER TABLE Cuenca ADD Color NVARCHAR(10) NULL;

-- Subcuenca: Agregar ArchivoKml, Color
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Subcuenca') AND name = 'ArchivoKml')
    ALTER TABLE Subcuenca ADD ArchivoKml NVARCHAR(255) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Subcuenca') AND name = 'Color')
    ALTER TABLE Subcuenca ADD Color NVARCHAR(10) NULL;

-- Migrar datos existentes de appsettings (opcional - adaptar nombres si difieren)
-- UPDATE Cuenca SET Codigo = 'ang', Color = '#1565C0' WHERE Nombre = 'Río Grijalva-Concordia';
-- UPDATE Cuenca SET Codigo = 'mmt', Color = '#7B1FA2' WHERE Nombre = 'Río Grijalva-Tuxtla Gutiérrez';
-- UPDATE Cuenca SET Codigo = 'mps', Color = '#00838F' WHERE Nombre = 'Río Grijalva-Villa Hermosa';
-- UPDATE Cuenca SET Codigo = 'pea', Color = '#AD1457' WHERE Nombre = 'Río Grijalva-Peñitas';

PRINT 'Migración completada: columnas KML agregadas a Cuenca y Subcuenca.';
