#!/usr/bin/env python3
"""
Crea las tablas del módulo BHG (Boletín Hidrológico Grijalva) en TimescaleDB.

Tablas:
  - bhg_presa_diario:    Niveles, aportaciones, extracciones, generación diaria por presa
  - bhg_estacion_diario: Lecturas diarias de estaciones convencionales
  - bhg_archivo:         Registro de archivos BHG procesados

Uso:
  python setup_bhg_tables.py
"""
import configparser, os, sys
import psycopg2

config = configparser.ConfigParser()
config.read(os.path.join(os.path.dirname(__file__), 'config.ini'))

PG_HOST = config.get('timescaledb', 'host')
PG_PORT = config.getint('timescaledb', 'port', fallback=5432)
PG_USER = config.get('timescaledb', 'user')
PG_PASSWORD = config.get('timescaledb', 'password')
PG_DATABASE = config.get('timescaledb', 'database')

SQL = """
-- ================================================================
-- Módulo BHG - Boletín Hidrológico Grijalva
-- ================================================================

-- 1. Datos diarios por presa/embalse
CREATE TABLE IF NOT EXISTS bhg_presa_diario (
    ts                      DATE NOT NULL,
    presa                   TEXT NOT NULL,  -- ANGOSTURA, CHICOASEN, MALPASO, PEÑITAS, CANAL_JG
    -- Niveles
    nivel                   DOUBLE PRECISION,  -- msnm a las 6:00h
    curva_guia              DOUBLE PRECISION,  -- msnm curva guía
    diff_curva_guia         DOUBLE PRECISION,  -- diferencia nivel - curva guía
    vol_almacenado          DOUBLE PRECISION,  -- Mill m3
    pct_llenado_namo        DOUBLE PRECISION,  -- % llenado al NAMO
    pct_llenado_name        DOUBLE PRECISION,  -- % llenado al NAME
    -- Aportaciones cuenca propia
    aportacion_vol          DOUBLE PRECISION,  -- Mill m3
    aportacion_q            DOUBLE PRECISION,  -- m3/s
    -- Extracción total
    extraccion_vol          DOUBLE PRECISION,  -- Mill m3
    extraccion_q            DOUBLE PRECISION,  -- m3/s
    -- Generación
    generacion_gwh          DOUBLE PRECISION,  -- GWh
    factor_planta           DOUBLE PRECISION,  -- %
    PRIMARY KEY (ts, presa)
);

-- 2. Datos diarios por estación convencional
CREATE TABLE IF NOT EXISTS bhg_estacion_diario (
    ts                      DATE NOT NULL,
    estacion                TEXT NOT NULL,
    subcuenca               TEXT,
    -- Lecturas
    precip_24h              DOUBLE PRECISION,  -- mm
    precip_acum_mensual     DOUBLE PRECISION,  -- mm acumulada del mes
    escala                  DOUBLE PRECISION,  -- m
    gasto                   DOUBLE PRECISION,  -- m3/s
    evaporacion             DOUBLE PRECISION,  -- mm
    temp_max                DOUBLE PRECISION,  -- °C
    temp_min                DOUBLE PRECISION,  -- °C
    temp_amb                DOUBLE PRECISION,  -- °C
    PRIMARY KEY (ts, estacion)
);

-- 3. Registro de archivos procesados
CREATE TABLE IF NOT EXISTS bhg_archivo (
    id                      SERIAL PRIMARY KEY,
    fecha                   DATE NOT NULL,
    nombre_archivo          TEXT NOT NULL,
    procesado_ts            TIMESTAMPTZ DEFAULT NOW(),
    mes                     INT,
    anio                    INT,
    dias_con_datos          INT DEFAULT 0,
    num_estaciones          INT DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_bhg_archivo_fecha ON bhg_archivo(fecha);

-- Índices útiles
CREATE INDEX IF NOT EXISTS idx_bhg_presa_presa ON bhg_presa_diario(presa);
CREATE INDEX IF NOT EXISTS idx_bhg_estacion_sub ON bhg_estacion_diario(subcuenca);
CREATE INDEX IF NOT EXISTS idx_bhg_estacion_est ON bhg_estacion_diario(estacion);

-- Convertir a hypertables (TimescaleDB) - solo si la extensión existe
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
"""

def main():
    print(f"Conectando a {PG_HOST}:{PG_PORT}/{PG_DATABASE}...")
    conn = psycopg2.connect(
        host=PG_HOST, port=PG_PORT, user=PG_USER,
        password=PG_PASSWORD, database=PG_DATABASE
    )
    conn.autocommit = True
    cur = conn.cursor()

    try:
        cur.execute(SQL)
    except Exception as e:
        print(f"  Error: {e}")

    # Verificar tablas creadas
    cur.execute("""
        SELECT table_name FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name LIKE 'bhg_%'
        ORDER BY table_name
    """)
    tables = [r[0] for r in cur.fetchall()]
    print(f"Tablas BHG creadas: {tables}")

    cur.close()
    conn.close()
    print("Setup BHG completado.")

if __name__ == '__main__':
    main()
