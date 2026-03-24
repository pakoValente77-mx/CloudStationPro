-- =============================================
-- CloudStation: Script de despliegue a producción
-- Modificaciones al módulo de Cuencas/Subcuencas
-- Fecha: 2026-03-24
-- Base de datos: IGSCLOUD (SQL Server)
-- =============================================
-- INSTRUCCIONES:
--   1. Hacer BACKUP de la base antes de ejecutar
--   2. Ejecutar en orden, cada bloque es idempotente (seguro re-ejecutar)
--   3. Verificar resultados al final
-- =============================================

USE IGSCLOUD;
GO

PRINT '=== INICIO: Deploy Cuencas/Subcuencas ==='
PRINT ''

-- =============================================
-- PASO 1: Agregar columnas KML a Cuenca
--         (Codigo, ArchivoKml, Color)
-- =============================================
PRINT '>> Paso 1: Columnas KML en Cuenca...'

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Cuenca') AND name = 'Codigo')
BEGIN
    ALTER TABLE Cuenca ADD Codigo NVARCHAR(20) NULL;
    PRINT '   + Cuenca.Codigo agregada'
END
ELSE PRINT '   = Cuenca.Codigo ya existe'

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Cuenca') AND name = 'ArchivoKml')
BEGIN
    ALTER TABLE Cuenca ADD ArchivoKml NVARCHAR(255) NULL;
    PRINT '   + Cuenca.ArchivoKml agregada'
END
ELSE PRINT '   = Cuenca.ArchivoKml ya existe'

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Cuenca') AND name = 'Color')
BEGIN
    ALTER TABLE Cuenca ADD Color NVARCHAR(10) NULL;
    PRINT '   + Cuenca.Color agregada'
END
ELSE PRINT '   = Cuenca.Color ya existe'

-- =============================================
-- PASO 2: Agregar columnas KML a Subcuenca
--         (ArchivoKml, Color)
-- =============================================
PRINT '>> Paso 2: Columnas KML en Subcuenca...'

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Subcuenca') AND name = 'ArchivoKml')
BEGIN
    ALTER TABLE Subcuenca ADD ArchivoKml NVARCHAR(255) NULL;
    PRINT '   + Subcuenca.ArchivoKml agregada'
END
ELSE PRINT '   = Subcuenca.ArchivoKml ya existe'

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Subcuenca') AND name = 'Color')
BEGIN
    ALTER TABLE Subcuenca ADD Color NVARCHAR(10) NULL;
    PRINT '   + Subcuenca.Color agregada'
END
ELSE PRINT '   = Subcuenca.Color ya existe'

-- =============================================
-- PASO 3: Agregar columna VerEnMapa a Cuenca
--         (controla visibilidad en el mapa)
-- =============================================
PRINT '>> Paso 3: VerEnMapa en Cuenca...'

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Cuenca') AND name = 'VerEnMapa')
BEGIN
    ALTER TABLE Cuenca ADD VerEnMapa BIT NOT NULL CONSTRAINT DF_Cuenca_VerEnMapa DEFAULT 0;
    PRINT '   + Cuenca.VerEnMapa agregada (default: 0)'
END
ELSE PRINT '   = Cuenca.VerEnMapa ya existe'

-- =============================================
-- PASO 4: Agregar columna VerEnMapa a Subcuenca
--         (controla visibilidad en el mapa)
-- =============================================
PRINT '>> Paso 4: VerEnMapa en Subcuenca...'

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Subcuenca') AND name = 'VerEnMapa')
BEGIN
    ALTER TABLE Subcuenca ADD VerEnMapa BIT NOT NULL CONSTRAINT DF_Subcuenca_VerEnMapa DEFAULT 0;
    PRINT '   + Subcuenca.VerEnMapa agregada (default: 0)'
END
ELSE PRINT '   = Subcuenca.VerEnMapa ya existe'

GO
-- Nuevo batch para que SQL Server reconozca las columnas recién agregadas

-- =============================================
-- PASO 5: Migrar datos de cuencas conocidas
--         (Codigo y Color desde appsettings)
--         Solo actualiza si Codigo está vacío
-- =============================================
PRINT '>> Paso 5: Migrar Codigo/Color de cuencas existentes...'

UPDATE Cuenca SET Codigo = 'ang', Color = '#1565C0'
WHERE Nombre = N'Río Grijalva-Concordia' AND (Codigo IS NULL OR Codigo = '');

UPDATE Cuenca SET Codigo = 'mmt', Color = '#7B1FA2'
WHERE Nombre = N'Río Grijalva-Tuxtla Gutiérrez' AND (Codigo IS NULL OR Codigo = '');

UPDATE Cuenca SET Codigo = 'mps', Color = '#00838F'
WHERE Nombre = N'Río Grijalva-Villa Hermosa' AND (Codigo IS NULL OR Codigo = '');

UPDATE Cuenca SET Codigo = 'pea', Color = '#AD1457'
WHERE Nombre = N'Río Grijalva-Peñitas' AND (Codigo IS NULL OR Codigo = '');

PRINT '   Filas actualizadas: ' + CAST(@@ROWCOUNT AS NVARCHAR(10))

GO

-- =============================================
-- PASO 6: Activar VerEnMapa para cuencas que
--         tienen KML y Codigo configurado
-- =============================================
PRINT '>> Paso 6: Activar VerEnMapa donde corresponda...'

UPDATE Cuenca SET VerEnMapa = 1
WHERE Activo = 1
  AND Codigo IS NOT NULL AND Codigo != ''
  AND ArchivoKml IS NOT NULL AND ArchivoKml != ''
  AND VerEnMapa = 0;

PRINT '   Cuencas activadas en mapa: ' + CAST(@@ROWCOUNT AS NVARCHAR(10))

UPDATE Subcuenca SET VerEnMapa = 1
WHERE Activo = 1
  AND ArchivoKml IS NOT NULL AND ArchivoKml != ''
  AND VerEnMapa = 0;

PRINT '   Subcuencas activadas en mapa: ' + CAST(@@ROWCOUNT AS NVARCHAR(10))

GO

-- =============================================
-- VERIFICACIÓN: Resumen del estado final
-- =============================================
PRINT ''
PRINT '=== VERIFICACIÓN ==='
PRINT ''

SELECT 'CUENCAS' AS Tabla,
       COUNT(*) AS Total,
       SUM(CASE WHEN Activo = 1 THEN 1 ELSE 0 END) AS Activas,
       SUM(CASE WHEN VerEnMapa = 1 THEN 1 ELSE 0 END) AS VisiblesEnMapa,
       SUM(CASE WHEN ArchivoKml IS NOT NULL AND ArchivoKml != '' THEN 1 ELSE 0 END) AS ConKml,
       SUM(CASE WHEN Codigo IS NOT NULL AND Codigo != '' THEN 1 ELSE 0 END) AS ConCodigo
FROM Cuenca;

SELECT 'SUBCUENCAS' AS Tabla,
       COUNT(*) AS Total,
       SUM(CASE WHEN Activo = 1 THEN 1 ELSE 0 END) AS Activas,
       SUM(CASE WHEN VerEnMapa = 1 THEN 1 ELSE 0 END) AS VisiblesEnMapa,
       SUM(CASE WHEN ArchivoKml IS NOT NULL AND ArchivoKml != '' THEN 1 ELSE 0 END) AS ConKml
FROM Subcuenca;

-- Detalle de cuencas
SELECT Id, Nombre, Codigo, Color, Activo, VerEnMapa, ArchivoKml
FROM Cuenca
ORDER BY Nombre;

-- Detalle de subcuencas
SELECT sc.Id, sc.Nombre, c.Nombre AS Cuenca, sc.Color, sc.Activo, sc.VerEnMapa, sc.ArchivoKml
FROM Subcuenca sc
INNER JOIN Cuenca c ON sc.IdCuenca = c.Id
ORDER BY c.Nombre, sc.Nombre;

PRINT ''
PRINT '=== FIN: Deploy Cuencas/Subcuencas completado ==='
GO
