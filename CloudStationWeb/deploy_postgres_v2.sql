-- =====================================================================
-- CLOUDSTATION PIH - Script PostgreSQL/TimescaleDB (Producción)
-- Base de datos: mycloud_timescale
-- Fecha: 2026-04-01
-- Descripción: Cambios incrementales para producción
-- INSTRUCCIONES:
--   1. Conectar con: psql -h atlas16.ddns.net -p 5432 -U postgres -d mycloud_timescale
--   2. Ejecutar este script: \i deploy_postgres_v2.sql
-- =====================================================================

-- =============================================================================
-- 1. dcp_headers: Agregar columna raw_message si no existe
--    Almacena el mensaje DOMSAT completo para exportación MIS
-- =============================================================================
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'dcp_headers' 
          AND column_name = 'raw_message'
    ) THEN
        ALTER TABLE public.dcp_headers ADD COLUMN raw_message TEXT;
        RAISE NOTICE 'Columna raw_message agregada a dcp_headers';
    ELSE
        RAISE NOTICE 'Columna raw_message ya existe en dcp_headers';
    END IF;
END $$;

-- =============================================================================
-- 2. Verificación
-- =============================================================================
SELECT column_name, data_type, is_nullable
FROM information_schema.columns 
WHERE table_name = 'dcp_headers'
ORDER BY ordinal_position;

SELECT 'dcp_headers' AS tabla, COUNT(*) AS registros FROM public.dcp_headers;
SELECT 'raw_message poblados' AS info, COUNT(*) AS total 
FROM public.dcp_headers WHERE raw_message IS NOT NULL;

-- =====================================================================
-- FIN
-- =====================================================================
