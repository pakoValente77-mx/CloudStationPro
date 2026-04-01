-- =====================================================================
-- CLOUDSTATION PIH - Script de Despliegue PostgreSQL/TimescaleDB
-- Base de datos: mycloud_timescale
-- Fecha: 2026-03-31
-- Descripción: Tablas nuevas que pueden no existir en producción
-- INSTRUCCIONES: Ejecutar con psql contra la BD mycloud_timescale
-- =====================================================================

-- =====================================================================
-- 1. Tabla lluvia_acumulada (precipitación acumulada 24h y periodo)
-- =====================================================================
CREATE TABLE IF NOT EXISTS public.lluvia_acumulada (
    id_asignado     CHARACTER VARYING(50) NOT NULL,
    dcp_id          CHARACTER VARYING(20) NOT NULL,
    sensor_id       CHARACTER VARYING(20) NOT NULL,
    variable        CHARACTER VARYING(50),
    periodo_inicio  TIMESTAMP WITH TIME ZONE NOT NULL,
    periodo_fin     TIMESTAMP WITH TIME ZONE NOT NULL,
    tipo_periodo    CHARACTER VARYING(10) NOT NULL,
    acumulado       REAL DEFAULT 0,
    horas_con_dato  INTEGER DEFAULT 0,
    ultima_actualizacion TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    CONSTRAINT lluvia_acumulada_pkey 
        PRIMARY KEY (id_asignado, sensor_id, tipo_periodo)
);

CREATE INDEX IF NOT EXISTS idx_lluvia_tipo_periodo 
ON public.lluvia_acumulada (tipo_periodo);

-- =====================================================================
-- 2. Tabla pronostico_lluvia (pronóstico de precipitación)
-- =====================================================================
CREATE TABLE IF NOT EXISTS public.pronostico_lluvia (
    id SERIAL PRIMARY KEY,
    cuenca_key VARCHAR(50) NOT NULL,
    fecha_pronostico TIMESTAMP WITH TIME ZONE NOT NULL,
    hora_pronostico INTEGER NOT NULL,
    valor_mm REAL,
    modelo VARCHAR(50),
    fecha_emision TIMESTAMP WITH TIME ZONE,
    fecha_insercion TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_pronostico_cuenca 
ON public.pronostico_lluvia (cuenca_key, fecha_pronostico);

-- =====================================================================
-- 3. Tabla funvasos_horario (datos horarios de presas)
-- =====================================================================
CREATE TABLE IF NOT EXISTS public.funvasos_horario (
    ts TIMESTAMP NOT NULL,
    presa CHARACTER VARYING(100) NOT NULL,
    hora SMALLINT NOT NULL,
    elevacion REAL,
    almacenamiento REAL,
    diferencia REAL,
    aportaciones_q REAL,
    aportaciones_v REAL,
    extracciones_turb_q REAL,
    extracciones_turb_v REAL,
    extracciones_vert_q REAL,
    extracciones_vert_v REAL,
    extracciones_total_q REAL,
    extracciones_total_v REAL,
    generacion REAL,
    num_unidades SMALLINT,
    aportacion_cuenca_propia REAL,
    aportacion_promedio REAL,
    CONSTRAINT funvasos_horario_pkey PRIMARY KEY (ts, presa, hora)
);

CREATE INDEX IF NOT EXISTS idx_funvasos_presa
ON public.funvasos_horario(presa);

CREATE INDEX IF NOT EXISTS idx_funvasos_ts
ON public.funvasos_horario(ts DESC);

-- =====================================================================
-- Verificación
-- =====================================================================
SELECT 'lluvia_acumulada' AS tabla, COUNT(*) AS registros FROM public.lluvia_acumulada
UNION ALL
SELECT 'pronostico_lluvia', COUNT(*) FROM public.pronostico_lluvia
UNION ALL
SELECT 'funvasos_horario', COUNT(*) FROM public.funvasos_horario;
