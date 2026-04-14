-- =====================================================================
-- CLOUDSTATION PIH - Script COMPLETO PostgreSQL/TimescaleDB (Producción)
-- Base de datos: mycloud_timescale
-- Fecha: 2026-04-13
-- Versión: v3.1
-- Descripción: Script completo desde cero para servidor nuevo.
--              Cada bloque es idempotente (puede re-ejecutarse sin daño).
-- INSTRUCCIONES:
--   1. Crear BD:  CREATE DATABASE mycloud_timescale;
--   2. Conectar:  psql -h <HOST> -p 5432 -U postgres -d mycloud_timescale
--   3. Ejecutar:  \i deploy_timescale_v2.sql
-- =====================================================================

-- =============================================================================
-- 0. Extensión TimescaleDB
-- =============================================================================
CREATE EXTENSION IF NOT EXISTS timescaledb CASCADE;

-- =============================================================================
-- 1. bitacora_goes (registro de transmisiones GOES recibidas)
-- =============================================================================
CREATE TABLE IF NOT EXISTS public.bitacora_goes (
    id SERIAL PRIMARY KEY,
    dcp_id CHARACTER VARYING(20) NOT NULL,
    timestamp_utc TIMESTAMP WITH TIME ZONE NOT NULL,
    timestamp_msg TIMESTAMP WITH TIME ZONE,
    servidor CHARACTER VARYING(200),
    exito BOOLEAN
);

-- =============================================================================
-- 2. dcp_headers (cabeceras DOMSAT de mensajes GOES)
-- =============================================================================
CREATE TABLE IF NOT EXISTS public.dcp_headers (
    dcp_id CHARACTER VARYING(20) NOT NULL,
    timestamp_msg TIMESTAMP WITH TIME ZONE NOT NULL,
    ts TIMESTAMP WITH TIME ZONE,
    failure_code CHARACTER VARYING(10),
    signal_strength SMALLINT,
    frequency_offset CHARACTER VARYING(10),
    mod_index CHARACTER VARYING(10),
    data_quality CHARACTER VARYING(10),
    channel SMALLINT,
    spacecraft CHARACTER VARYING(10),
    data_source CHARACTER VARYING(10),
    raw_message TEXT,
    CONSTRAINT dcp_headers_pkey PRIMARY KEY (dcp_id, timestamp_msg)
);

-- Agregar raw_message si la tabla ya existía sin ella
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'dcp_headers' AND column_name = 'raw_message'
    ) THEN
        ALTER TABLE public.dcp_headers ADD COLUMN raw_message TEXT;
        RAISE NOTICE 'Columna raw_message agregada a dcp_headers';
    END IF;
END $$;

-- =============================================================================
-- 3. dcp_datos (mediciones de sensores - Hypertable TimescaleDB)
-- =============================================================================
CREATE TABLE IF NOT EXISTS public.dcp_datos (
    ts TIMESTAMP WITH TIME ZONE NOT NULL,
    dcp_id CHARACTER VARYING(20) NOT NULL,
    id_asignado CHARACTER VARYING(50),
    sensor_id CHARACTER VARYING(20) NOT NULL,
    variable CHARACTER VARYING(50),
    valor REAL,
    tipo CHARACTER VARYING(50),
    valido BOOLEAN,
    descripcion CHARACTER VARYING(200),
    anio SMALLINT,
    mes SMALLINT,
    dia SMALLINT,
    hora SMALLINT,
    minuto SMALLINT,
    CONSTRAINT dcp_datos_pkey PRIMARY KEY (ts, dcp_id, sensor_id)
);

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM timescaledb_information.hypertables WHERE hypertable_name = 'dcp_datos') THEN
    PERFORM create_hypertable('public.dcp_datos', 'ts', migrate_data => true);
    RAISE NOTICE 'dcp_datos convertida a hypertable';
  END IF;
END $$;

-- =============================================================================
-- 4. resumen_horario (agregados por hora - Hypertable)
-- =============================================================================
CREATE TABLE IF NOT EXISTS public.resumen_horario (
    ts TIMESTAMP WITH TIME ZONE NOT NULL,
    dcp_id CHARACTER VARYING(20) NOT NULL,
    sensor_id CHARACTER VARYING(20) NOT NULL,
    id_asignado CHARACTER VARYING(50),
    variable CHARACTER VARYING(50),
    tipo CHARACTER VARYING(50),
    suma REAL,
    conteo INTEGER,
    promedio REAL,
    minimo REAL,
    maximo REAL,
    acumulado REAL,
    CONSTRAINT resumen_horario_pkey PRIMARY KEY (ts, dcp_id, sensor_id)
);

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM timescaledb_information.hypertables WHERE hypertable_name = 'resumen_horario') THEN
    PERFORM create_hypertable('public.resumen_horario', 'ts', migrate_data => true);
    RAISE NOTICE 'resumen_horario convertida a hypertable';
  END IF;
END $$;

-- =============================================================================
-- 5. resumen_diario (agregados por día - Hypertable)
-- =============================================================================
CREATE TABLE IF NOT EXISTS public.resumen_diario (
    fecha DATE NOT NULL,
    dcp_id CHARACTER VARYING(20) NOT NULL,
    sensor_id CHARACTER VARYING(20) NOT NULL,
    id_asignado CHARACTER VARYING(50),
    variable CHARACTER VARYING(50),
    tipo CHARACTER VARYING(50),
    suma REAL,
    conteo INTEGER,
    promedio REAL,
    minimo REAL,
    maximo REAL,
    acumulado REAL,
    CONSTRAINT resumen_diario_pkey PRIMARY KEY (fecha, dcp_id, sensor_id)
);

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM timescaledb_information.hypertables WHERE hypertable_name = 'resumen_diario') THEN
    PERFORM create_hypertable('public.resumen_diario', 'fecha', migrate_data => true);
    RAISE NOTICE 'resumen_diario convertida a hypertable';
  END IF;
END $$;

-- =============================================================================
-- 6. lluvia_acumulada (precipitación acumulada 24h/periodo)
-- =============================================================================
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

-- =============================================================================
-- 7. pronostico_lluvia (pronóstico de precipitación por cuenca)
-- =============================================================================
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

-- =============================================================================
-- 8. funvasos_horario (datos horarios de presas)
-- =============================================================================
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

CREATE INDEX IF NOT EXISTS idx_funvasos_presa ON public.funvasos_horario(presa);
CREATE INDEX IF NOT EXISTS idx_funvasos_ts ON public.funvasos_horario(ts DESC);

-- =============================================================================
-- 9. ultimas_mediciones (cache de última medición por sensor)
-- =============================================================================
CREATE TABLE IF NOT EXISTS public.ultimas_mediciones (
    dcp_id CHARACTER VARYING NOT NULL,
    variable CHARACTER VARYING NOT NULL,
    ts TIMESTAMP WITH TIME ZONE,
    valor REAL,
    tipo CHARACTER VARYING,
    PRIMARY KEY (dcp_id, variable)
);

-- =============================================================================
-- 10. estatus_estaciones (semáforo de comunicaciones)
-- =============================================================================
CREATE TABLE IF NOT EXISTS public.estatus_estaciones (
    dcp_id CHARACTER VARYING(20) NOT NULL PRIMARY KEY,
    fecha_calculo TIMESTAMP WITH TIME ZONE,
    intervalo_minutos INTEGER DEFAULT 60,
    mensajes_esperados_48h INTEGER,
    mensajes_recibidos_48h INTEGER DEFAULT 0,
    color_estatus CHARACTER VARYING(20) DEFAULT 'NEGRO',
    fecha_ultima_tx TIMESTAMP WITH TIME ZONE,
    servidor CHARACTER VARYING(50)
);

-- =============================================================================
-- 11. alertas_precipitacion (alertas por umbral de precipitación)
-- =============================================================================
CREATE TABLE IF NOT EXISTS public.alertas_precipitacion (
    ts                  TIMESTAMPTZ NOT NULL,
    id_asignado         VARCHAR(50) NOT NULL,
    dcp_id              VARCHAR(20) NOT NULL,
    sensor_id           VARCHAR(20) NOT NULL,
    estacion_nombre     VARCHAR(200),
    umbral_id           BIGINT NOT NULL,
    umbral_nombre       VARCHAR(100),
    valor_referencia    REAL,
    valor_medido        REAL,
    operador            VARCHAR(2),
    periodo_minutos     INT,
    color               VARCHAR(50),
    activa              BOOLEAN DEFAULT TRUE,
    notificada          BOOLEAN DEFAULT FALSE,
    CONSTRAINT alertas_precipitacion_pkey
        PRIMARY KEY (ts, dcp_id, sensor_id, umbral_id)
);

CREATE INDEX IF NOT EXISTS idx_alertas_precip_estacion
ON public.alertas_precipitacion (id_asignado, ts DESC);

CREATE INDEX IF NOT EXISTS idx_alertas_precip_activa
ON public.alertas_precipitacion (activa, notificada);

-- =============================================================================
-- 12. eventos_lluvia (tracking de eventos de precipitación)
-- =============================================================================
CREATE TABLE IF NOT EXISTS public.eventos_lluvia (
    id                   BIGSERIAL PRIMARY KEY,
    id_asignado          VARCHAR(50) NOT NULL,
    dcp_id               VARCHAR(20) NOT NULL,
    sensor_id            VARCHAR(20) NOT NULL,
    estacion_nombre      VARCHAR(200),
    inicio               TIMESTAMPTZ NOT NULL,
    fin                  TIMESTAMPTZ,
    acumulado_mm         REAL DEFAULT 0,
    intensidad_max_mmh   REAL DEFAULT 0,
    duracion_minutos     INT DEFAULT 0,
    estado               VARCHAR(20) DEFAULT 'activo',
    sospechoso           BOOLEAN DEFAULT FALSE,
    motivo_sospecha      TEXT,
    ceros_consecutivos   INT DEFAULT 0,
    ultimo_ts_procesado  TIMESTAMPTZ,
    ultima_actualizacion TIMESTAMPTZ DEFAULT NOW()
);

-- Agregar columnas si tabla ya existía sin ellas
DO $$ BEGIN
    ALTER TABLE public.eventos_lluvia ADD COLUMN IF NOT EXISTS sospechoso BOOLEAN DEFAULT FALSE;
    ALTER TABLE public.eventos_lluvia ADD COLUMN IF NOT EXISTS motivo_sospecha TEXT;
END $$;

CREATE INDEX IF NOT EXISTS idx_eventos_lluvia_activo
ON public.eventos_lluvia (id_asignado, sensor_id, estado);

-- =============================================================================
-- 13. precipitacion_cuenca (promedios de precipitación por cuenca/subcuenca)
-- =============================================================================
CREATE TABLE IF NOT EXISTS public.precipitacion_cuenca (
    ts                  TIMESTAMPTZ NOT NULL,
    tipo                VARCHAR(10) NOT NULL,
    nombre              VARCHAR(200) NOT NULL,
    promedio_mm         REAL DEFAULT 0,
    max_mm              REAL DEFAULT 0,
    min_mm              REAL DEFAULT 0,
    estaciones_con_dato INTEGER DEFAULT 0,
    estaciones_total    INTEGER DEFAULT 0,
    semaforo            VARCHAR(10),
    ultima_actualizacion TIMESTAMPTZ DEFAULT NOW(),
    CONSTRAINT precipitacion_cuenca_pkey
        PRIMARY KEY (ts, tipo, nombre)
);

CREATE INDEX IF NOT EXISTS idx_precip_cuenca_ts
ON public.precipitacion_cuenca (ts DESC, tipo);

-- =============================================================================
-- 14. Módulo BHG - Boletín Hidrológico Grijalva
-- =============================================================================

-- 14a. Datos diarios por presa/embalse
CREATE TABLE IF NOT EXISTS public.bhg_presa_diario (
    ts                      DATE NOT NULL,
    presa                   TEXT NOT NULL,
    nivel                   DOUBLE PRECISION,
    curva_guia              DOUBLE PRECISION,
    diff_curva_guia         DOUBLE PRECISION,
    vol_almacenado          DOUBLE PRECISION,
    pct_llenado_namo        DOUBLE PRECISION,
    pct_llenado_name        DOUBLE PRECISION,
    aportacion_vol          DOUBLE PRECISION,
    aportacion_q            DOUBLE PRECISION,
    extraccion_vol          DOUBLE PRECISION,
    extraccion_q            DOUBLE PRECISION,
    generacion_gwh          DOUBLE PRECISION,
    factor_planta           DOUBLE PRECISION,
    PRIMARY KEY (ts, presa)
);

-- 14b. Datos diarios por estación convencional
CREATE TABLE IF NOT EXISTS public.bhg_estacion_diario (
    ts                      DATE NOT NULL,
    estacion                TEXT NOT NULL,
    subcuenca               TEXT,
    precip_24h              DOUBLE PRECISION,
    precip_acum_mensual     DOUBLE PRECISION,
    escala                  DOUBLE PRECISION,
    gasto                   DOUBLE PRECISION,
    evaporacion             DOUBLE PRECISION,
    temp_max                DOUBLE PRECISION,
    temp_min                DOUBLE PRECISION,
    temp_amb                DOUBLE PRECISION,
    PRIMARY KEY (ts, estacion)
);

-- 14c. Registro de archivos BHG procesados
CREATE TABLE IF NOT EXISTS public.bhg_archivo (
    id                      SERIAL PRIMARY KEY,
    fecha                   DATE NOT NULL,
    nombre_archivo          TEXT NOT NULL,
    procesado_ts            TIMESTAMPTZ DEFAULT NOW(),
    mes                     INT,
    anio                    INT,
    dias_con_datos          INT DEFAULT 0,
    num_estaciones          INT DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_bhg_archivo_fecha ON public.bhg_archivo(fecha);

CREATE INDEX IF NOT EXISTS idx_bhg_presa_presa ON public.bhg_presa_diario(presa);
CREATE INDEX IF NOT EXISTS idx_bhg_estacion_sub ON public.bhg_estacion_diario(subcuenca);
CREATE INDEX IF NOT EXISTS idx_bhg_estacion_est ON public.bhg_estacion_diario(estacion);

-- Convertir BHG a hypertables
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'timescaledb') THEN
        PERFORM create_hypertable('bhg_presa_diario', 'ts',
            chunk_time_interval => INTERVAL '1 year',
            if_not_exists => TRUE,
            migrate_data => TRUE);
        PERFORM create_hypertable('bhg_estacion_diario', 'ts',
            chunk_time_interval => INTERVAL '1 year',
            if_not_exists => TRUE,
            migrate_data => TRUE);
    END IF;
END $$;

-- =============================================================================
-- 15. Triggers y funciones
-- =============================================================================

-- Trigger: actualizar estatus de estación al recibir transmisión
CREATE OR REPLACE FUNCTION public.trg_actualizar_estatus()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
DECLARE
    v_rec_count INT;
    v_intervalo INT;
    v_status VARCHAR(20);
    v_window_start TIMESTAMP WITH TIME ZONE;
BEGIN
    v_window_start := NOW() - INTERVAL '48 hours';
    SELECT COALESCE(intervalo_minutos, 60) INTO v_intervalo
    FROM public.estatus_estaciones
    WHERE dcp_id = NEW.dcp_id;
    IF v_intervalo IS NULL THEN v_intervalo := 60; END IF;
    SELECT COUNT(*) INTO v_rec_count
    FROM public.bitacora_goes
    WHERE dcp_id = NEW.dcp_id
      AND timestamp_utc >= v_window_start;
    IF v_rec_count = 0 THEN
        v_status := 'NEGRO';
    ELSIF v_rec_count >= (48 * 60 / v_intervalo) THEN
        v_status := 'VERDE';
    ELSIF (48 * 60 / v_intervalo) - v_rec_count <= 2 THEN
        v_status := 'AMARILLO';
    ELSE
        v_status := 'ROJO';
    END IF;
    INSERT INTO public.estatus_estaciones (
        dcp_id, fecha_calculo, mensajes_recibidos_48h, 
        fecha_ultima_tx, servidor, color_estatus
    )
    VALUES (
        NEW.dcp_id, NOW(), v_rec_count, 
        NEW.timestamp_utc, NEW.servidor, v_status
    )
    ON CONFLICT (dcp_id) DO UPDATE SET
        fecha_calculo = EXCLUDED.fecha_calculo,
        mensajes_recibidos_48h = EXCLUDED.mensajes_recibidos_48h,
        fecha_ultima_tx = EXCLUDED.fecha_ultima_tx,
        servidor = EXCLUDED.servidor,
        color_estatus = EXCLUDED.color_estatus;
    RETURN NEW;
END;
$function$;

-- Trigger: actualizar última medición al insertar dato
CREATE OR REPLACE FUNCTION public.trg_actualizar_ultima_medicion()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
BEGIN
    INSERT INTO public.ultimas_mediciones (dcp_id, variable, ts, valor, tipo)
    VALUES (NEW.dcp_id, NEW.variable, NEW.ts, NEW.valor, NEW.tipo)
    ON CONFLICT (dcp_id, variable) DO UPDATE SET
        ts = CASE WHEN EXCLUDED.ts >= ultimas_mediciones.ts THEN EXCLUDED.ts ELSE ultimas_mediciones.ts END,
        valor = CASE WHEN EXCLUDED.ts >= ultimas_mediciones.ts THEN EXCLUDED.valor ELSE ultimas_mediciones.valor END,
        tipo = CASE WHEN EXCLUDED.ts >= ultimas_mediciones.ts THEN EXCLUDED.tipo ELSE ultimas_mediciones.tipo END;
    RETURN NEW;
END;
$function$;

-- Crear triggers (DROP + CREATE para idempotencia)
DROP TRIGGER IF EXISTS trg_update_estatus_on_insert ON public.bitacora_goes;
CREATE TRIGGER trg_update_estatus_on_insert 
    AFTER INSERT ON public.bitacora_goes 
    FOR EACH ROW EXECUTE FUNCTION public.trg_actualizar_estatus();

DROP TRIGGER IF EXISTS trg_update_last_val ON public.dcp_datos;
CREATE TRIGGER trg_update_last_val 
    AFTER INSERT ON public.dcp_datos 
    FOR EACH ROW EXECUTE FUNCTION public.trg_actualizar_ultima_medicion();

-- =============================================================================
-- Verificación final
-- =============================================================================
SELECT 'bitacora_goes' AS tabla, COUNT(*) AS registros FROM public.bitacora_goes
UNION ALL SELECT 'dcp_headers', COUNT(*) FROM public.dcp_headers
UNION ALL SELECT 'dcp_datos', COUNT(*) FROM public.dcp_datos
UNION ALL SELECT 'resumen_horario', COUNT(*) FROM public.resumen_horario
UNION ALL SELECT 'resumen_diario', COUNT(*) FROM public.resumen_diario
UNION ALL SELECT 'lluvia_acumulada', COUNT(*) FROM public.lluvia_acumulada
UNION ALL SELECT 'pronostico_lluvia', COUNT(*) FROM public.pronostico_lluvia
UNION ALL SELECT 'funvasos_horario', COUNT(*) FROM public.funvasos_horario
UNION ALL SELECT 'ultimas_mediciones', COUNT(*) FROM public.ultimas_mediciones
UNION ALL SELECT 'estatus_estaciones', COUNT(*) FROM public.estatus_estaciones
UNION ALL SELECT 'alertas_precipitacion', COUNT(*) FROM public.alertas_precipitacion
UNION ALL SELECT 'eventos_lluvia', COUNT(*) FROM public.eventos_lluvia
UNION ALL SELECT 'precipitacion_cuenca', COUNT(*) FROM public.precipitacion_cuenca
UNION ALL SELECT 'bhg_presa_diario', COUNT(*) FROM public.bhg_presa_diario
UNION ALL SELECT 'bhg_estacion_diario', COUNT(*) FROM public.bhg_estacion_diario
UNION ALL SELECT 'bhg_archivo', COUNT(*) FROM public.bhg_archivo
ORDER BY 1;

-- =====================================================================
-- FIN - DEPLOY TIMESCALEDB v3.1 COMPLETO (16 tablas)
-- =====================================================================
