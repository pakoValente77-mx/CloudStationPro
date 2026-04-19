#!/usr/bin/env python3
"""
CLOUDSTATION PIH - Deploy COMPLETO PostgreSQL/TimescaleDB (Producción)
Base de datos: mycloud_timescale
Fecha: 2026-04-16
Versión: v3.1

Equivale a deploy_timescale_v2.sql pero ejecutable con Python.
Cada bloque es idempotente (puede re-ejecutarse sin daño).

Uso:
  python deploy_timescale_v3.py
"""

import configparser
import os
import sys
from datetime import datetime

try:
    import psycopg2
except ImportError:
    print("[ERROR] psycopg2 no instalado. Ejecutar: pip install psycopg2-binary")
    sys.exit(1)


def get_pg_config():
    """Lee config.ini para obtener credenciales de TimescaleDB."""
    config = configparser.ConfigParser()
    config_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "config.ini")
    if not os.path.exists(config_path):
        print(f"[ERROR] No se encontró {config_path}")
        sys.exit(1)
    config.read(config_path)
    return {
        "host": config.get("timescaledb", "host"),
        "port": config.getint("timescaledb", "port"),
        "user": config.get("timescaledb", "user"),
        "password": config.get("timescaledb", "password"),
        "database": config.get("timescaledb", "database"),
    }


def column_exists(cur, table, column):
    cur.execute("""
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = %s AND column_name = %s
    """, (table, column))
    return cur.fetchone() is not None


def table_exists(cur, table):
    cur.execute("""
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = %s
    """, (table,))
    return cur.fetchone() is not None


def is_hypertable(cur, table):
    cur.execute("""
        SELECT 1 FROM timescaledb_information.hypertables
        WHERE hypertable_name = %s
    """, (table,))
    return cur.fetchone() is not None


def run_deploy(conn):
    """Ejecuta el deploy completo de TimescaleDB."""
    cur = conn.cursor()
    ok = 0
    skip = 0

    # ==========================================================================
    # 0. Extensión TimescaleDB
    # ==========================================================================
    print("[0] Extensión TimescaleDB...")
    cur.execute("CREATE EXTENSION IF NOT EXISTS timescaledb CASCADE")
    conn.commit()
    print("    OK")

    # ==========================================================================
    # 1. bitacora_goes
    # ==========================================================================
    print("[1] bitacora_goes...")
    if not table_exists(cur, "bitacora_goes"):
        cur.execute("""
            CREATE TABLE public.bitacora_goes (
                id SERIAL PRIMARY KEY,
                dcp_id CHARACTER VARYING(20) NOT NULL,
                timestamp_utc TIMESTAMP WITH TIME ZONE NOT NULL,
                timestamp_msg TIMESTAMP WITH TIME ZONE,
                servidor CHARACTER VARYING(200),
                exito BOOLEAN
            )
        """)
        conn.commit()
        print("    + CREADA")
        ok += 1
    else:
        print("    = ya existe")
        skip += 1

    # ==========================================================================
    # 2. dcp_headers
    # ==========================================================================
    print("[2] dcp_headers...")
    if not table_exists(cur, "dcp_headers"):
        cur.execute("""
            CREATE TABLE public.dcp_headers (
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
            )
        """)
        conn.commit()
        print("    + CREADA")
        ok += 1
    else:
        print("    = ya existe")
        skip += 1
        if not column_exists(cur, "dcp_headers", "raw_message"):
            cur.execute("ALTER TABLE public.dcp_headers ADD COLUMN raw_message TEXT")
            conn.commit()
            print("    + raw_message AGREGADA")
            ok += 1

    # ==========================================================================
    # 3. dcp_datos (Hypertable)
    # ==========================================================================
    print("[3] dcp_datos...")
    if not table_exists(cur, "dcp_datos"):
        cur.execute("""
            CREATE TABLE public.dcp_datos (
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
            )
        """)
        conn.commit()
        print("    + CREADA")
        ok += 1
    else:
        print("    = ya existe")
        skip += 1

    if not is_hypertable(cur, "dcp_datos"):
        cur.execute("SELECT create_hypertable('public.dcp_datos', 'ts', migrate_data => true)")
        conn.commit()
        print("    + Convertida a hypertable")
        ok += 1

    # ==========================================================================
    # 4. resumen_horario (Hypertable)
    # ==========================================================================
    print("[4] resumen_horario...")
    if not table_exists(cur, "resumen_horario"):
        cur.execute("""
            CREATE TABLE public.resumen_horario (
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
            )
        """)
        conn.commit()
        print("    + CREADA")
        ok += 1
    else:
        print("    = ya existe")
        skip += 1

    if not is_hypertable(cur, "resumen_horario"):
        cur.execute("SELECT create_hypertable('public.resumen_horario', 'ts', migrate_data => true)")
        conn.commit()
        print("    + Convertida a hypertable")
        ok += 1

    # ==========================================================================
    # 5. resumen_diario (Hypertable)
    # ==========================================================================
    print("[5] resumen_diario...")
    if not table_exists(cur, "resumen_diario"):
        cur.execute("""
            CREATE TABLE public.resumen_diario (
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
            )
        """)
        conn.commit()
        print("    + CREADA")
        ok += 1
    else:
        print("    = ya existe")
        skip += 1

    if not is_hypertable(cur, "resumen_diario"):
        cur.execute("SELECT create_hypertable('public.resumen_diario', 'fecha', migrate_data => true)")
        conn.commit()
        print("    + Convertida a hypertable")
        ok += 1

    # ==========================================================================
    # 6. lluvia_acumulada
    # ==========================================================================
    print("[6] lluvia_acumulada...")
    if not table_exists(cur, "lluvia_acumulada"):
        cur.execute("""
            CREATE TABLE public.lluvia_acumulada (
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
            )
        """)
        conn.commit()
        print("    + CREADA")
        ok += 1
    else:
        print("    = ya existe")
        skip += 1

    cur.execute("CREATE INDEX IF NOT EXISTS idx_lluvia_tipo_periodo ON public.lluvia_acumulada (tipo_periodo)")
    conn.commit()

    # ==========================================================================
    # 7. pronostico_lluvia
    # ==========================================================================
    print("[7] pronostico_lluvia...")
    if not table_exists(cur, "pronostico_lluvia"):
        cur.execute("""
            CREATE TABLE public.pronostico_lluvia (
                id SERIAL PRIMARY KEY,
                cuenca_key VARCHAR(50) NOT NULL,
                fecha_pronostico TIMESTAMP WITH TIME ZONE NOT NULL,
                hora_pronostico INTEGER NOT NULL,
                valor_mm REAL,
                modelo VARCHAR(50),
                fecha_emision TIMESTAMP WITH TIME ZONE,
                fecha_insercion TIMESTAMP WITH TIME ZONE DEFAULT NOW()
            )
        """)
        conn.commit()
        print("    + CREADA")
        ok += 1
    else:
        print("    = ya existe")
        skip += 1

    cur.execute("CREATE INDEX IF NOT EXISTS idx_pronostico_cuenca ON public.pronostico_lluvia (cuenca_key, fecha_pronostico)")
    conn.commit()

    # ==========================================================================
    # 8. funvasos_horario
    # ==========================================================================
    print("[8] funvasos_horario...")
    if not table_exists(cur, "funvasos_horario"):
        cur.execute("""
            CREATE TABLE public.funvasos_horario (
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
            )
        """)
        conn.commit()
        print("    + CREADA")
        ok += 1
    else:
        print("    = ya existe")
        skip += 1

    cur.execute("CREATE INDEX IF NOT EXISTS idx_funvasos_presa ON public.funvasos_horario(presa)")
    cur.execute("CREATE INDEX IF NOT EXISTS idx_funvasos_ts ON public.funvasos_horario(ts DESC)")
    conn.commit()

    # ==========================================================================
    # 9. ultimas_mediciones
    # ==========================================================================
    print("[9] ultimas_mediciones...")
    if not table_exists(cur, "ultimas_mediciones"):
        cur.execute("""
            CREATE TABLE public.ultimas_mediciones (
                dcp_id CHARACTER VARYING NOT NULL,
                variable CHARACTER VARYING NOT NULL,
                ts TIMESTAMP WITH TIME ZONE,
                valor REAL,
                tipo CHARACTER VARYING,
                PRIMARY KEY (dcp_id, variable)
            )
        """)
        conn.commit()
        print("    + CREADA")
        ok += 1
    else:
        print("    = ya existe")
        skip += 1

    # ==========================================================================
    # 10. estatus_estaciones
    # ==========================================================================
    print("[10] estatus_estaciones...")
    if not table_exists(cur, "estatus_estaciones"):
        cur.execute("""
            CREATE TABLE public.estatus_estaciones (
                dcp_id CHARACTER VARYING(20) NOT NULL PRIMARY KEY,
                fecha_calculo TIMESTAMP WITH TIME ZONE,
                intervalo_minutos INTEGER DEFAULT 60,
                mensajes_esperados_48h INTEGER,
                mensajes_recibidos_48h INTEGER DEFAULT 0,
                color_estatus CHARACTER VARYING(20) DEFAULT 'NEGRO',
                fecha_ultima_tx TIMESTAMP WITH TIME ZONE,
                servidor CHARACTER VARYING(50)
            )
        """)
        conn.commit()
        print("    + CREADA")
        ok += 1
    else:
        print("    = ya existe")
        skip += 1

    # ==========================================================================
    # 11. alertas_precipitacion
    # ==========================================================================
    print("[11] alertas_precipitacion...")
    if not table_exists(cur, "alertas_precipitacion"):
        cur.execute("""
            CREATE TABLE public.alertas_precipitacion (
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
            )
        """)
        conn.commit()
        print("    + CREADA")
        ok += 1
    else:
        print("    = ya existe")
        skip += 1

    cur.execute("CREATE INDEX IF NOT EXISTS idx_alertas_precip_estacion ON public.alertas_precipitacion (id_asignado, ts DESC)")
    cur.execute("CREATE INDEX IF NOT EXISTS idx_alertas_precip_activa ON public.alertas_precipitacion (activa, notificada)")
    conn.commit()

    # ==========================================================================
    # 12. eventos_lluvia
    # ==========================================================================
    print("[12] eventos_lluvia...")
    if not table_exists(cur, "eventos_lluvia"):
        cur.execute("""
            CREATE TABLE public.eventos_lluvia (
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
            )
        """)
        conn.commit()
        print("    + CREADA")
        ok += 1
    else:
        print("    = ya existe")
        skip += 1
        # Agregar columnas nuevas si tabla existía sin ellas
        if not column_exists(cur, "eventos_lluvia", "sospechoso"):
            cur.execute("ALTER TABLE public.eventos_lluvia ADD COLUMN sospechoso BOOLEAN DEFAULT FALSE")
            conn.commit()
            print("    + sospechoso AGREGADA")
            ok += 1
        if not column_exists(cur, "eventos_lluvia", "motivo_sospecha"):
            cur.execute("ALTER TABLE public.eventos_lluvia ADD COLUMN motivo_sospecha TEXT")
            conn.commit()
            print("    + motivo_sospecha AGREGADA")
            ok += 1

    cur.execute("CREATE INDEX IF NOT EXISTS idx_eventos_lluvia_activo ON public.eventos_lluvia (id_asignado, sensor_id, estado)")
    conn.commit()

    # ==========================================================================
    # 13. precipitacion_cuenca
    # ==========================================================================
    print("[13] precipitacion_cuenca...")
    if not table_exists(cur, "precipitacion_cuenca"):
        cur.execute("""
            CREATE TABLE public.precipitacion_cuenca (
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
            )
        """)
        conn.commit()
        print("    + CREADA")
        ok += 1
    else:
        print("    = ya existe")
        skip += 1

    cur.execute("CREATE INDEX IF NOT EXISTS idx_precip_cuenca_ts ON public.precipitacion_cuenca (ts DESC, tipo)")
    conn.commit()

    # ==========================================================================
    # 14. Módulo BHG - Boletín Hidrológico Grijalva
    # ==========================================================================
    print("[14a] bhg_presa_diario...")
    if not table_exists(cur, "bhg_presa_diario"):
        cur.execute("""
            CREATE TABLE public.bhg_presa_diario (
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
            )
        """)
        conn.commit()
        print("    + CREADA")
        ok += 1
    else:
        print("    = ya existe")
        skip += 1

    print("[14b] bhg_estacion_diario...")
    if not table_exists(cur, "bhg_estacion_diario"):
        cur.execute("""
            CREATE TABLE public.bhg_estacion_diario (
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
            )
        """)
        conn.commit()
        print("    + CREADA")
        ok += 1
    else:
        print("    = ya existe")
        skip += 1

    print("[14c] bhg_archivo...")
    if not table_exists(cur, "bhg_archivo"):
        cur.execute("""
            CREATE TABLE public.bhg_archivo (
                id                      SERIAL PRIMARY KEY,
                fecha                   DATE NOT NULL,
                nombre_archivo          TEXT NOT NULL,
                procesado_ts            TIMESTAMPTZ DEFAULT NOW(),
                mes                     INT,
                anio                    INT,
                dias_con_datos          INT DEFAULT 0,
                num_estaciones          INT DEFAULT 0
            )
        """)
        conn.commit()
        print("    + CREADA")
        ok += 1
    else:
        print("    = ya existe")
        skip += 1

    cur.execute("CREATE UNIQUE INDEX IF NOT EXISTS idx_bhg_archivo_fecha ON public.bhg_archivo(fecha)")
    cur.execute("CREATE INDEX IF NOT EXISTS idx_bhg_presa_presa ON public.bhg_presa_diario(presa)")
    cur.execute("CREATE INDEX IF NOT EXISTS idx_bhg_estacion_sub ON public.bhg_estacion_diario(subcuenca)")
    cur.execute("CREATE INDEX IF NOT EXISTS idx_bhg_estacion_est ON public.bhg_estacion_diario(estacion)")
    conn.commit()

    # Convertir BHG a hypertables
    for tbl in ("bhg_presa_diario", "bhg_estacion_diario"):
        if not is_hypertable(cur, tbl):
            cur.execute(f"""
                SELECT create_hypertable('{tbl}', 'ts',
                    chunk_time_interval => INTERVAL '1 year',
                    if_not_exists => TRUE,
                    migrate_data => TRUE)
            """)
            conn.commit()
            print(f"    + {tbl} convertida a hypertable")
            ok += 1

    # ==========================================================================
    # 15. Triggers y funciones
    # ==========================================================================
    print("[15] Triggers y funciones...")

    # Función: actualizar estatus de estación
    cur.execute(r"""
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
        $function$
    """)
    conn.commit()
    print("    + trg_actualizar_estatus OK")

    # Función: actualizar última medición
    cur.execute(r"""
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
        $function$
    """)
    conn.commit()
    print("    + trg_actualizar_ultima_medicion OK")

    # Triggers (DROP + CREATE para idempotencia)
    cur.execute("DROP TRIGGER IF EXISTS trg_update_estatus_on_insert ON public.bitacora_goes")
    cur.execute("""
        CREATE TRIGGER trg_update_estatus_on_insert
            AFTER INSERT ON public.bitacora_goes
            FOR EACH ROW EXECUTE FUNCTION public.trg_actualizar_estatus()
    """)
    conn.commit()
    print("    + trigger trg_update_estatus_on_insert OK")

    cur.execute("DROP TRIGGER IF EXISTS trg_update_last_val ON public.dcp_datos")
    cur.execute("""
        CREATE TRIGGER trg_update_last_val
            AFTER INSERT ON public.dcp_datos
            FOR EACH ROW EXECUTE FUNCTION public.trg_actualizar_ultima_medicion()
    """)
    conn.commit()
    print("    + trigger trg_update_last_val OK")
    ok += 1

    cur.close()
    return ok, skip


def verify(conn):
    """Verificación final: conteo de registros por tabla."""
    cur = conn.cursor()
    tables = [
        "bitacora_goes", "dcp_headers", "dcp_datos",
        "resumen_horario", "resumen_diario", "lluvia_acumulada",
        "pronostico_lluvia", "funvasos_horario", "ultimas_mediciones",
        "estatus_estaciones", "alertas_precipitacion", "eventos_lluvia",
        "precipitacion_cuenca", "bhg_presa_diario", "bhg_estacion_diario",
        "bhg_archivo",
    ]
    print(f"\n{'Tabla':<30s} {'Registros':>12s}")
    print("-" * 44)
    for t in tables:
        try:
            cur.execute(f"SELECT COUNT(*) FROM public.{t}")  # noqa: S608 - table names are hardcoded
            count = cur.fetchone()[0]
            print(f"  {t:<28s} {count:>10,d}")
        except Exception:
            conn.rollback()
            print(f"  {t:<28s} {'ERROR':>10s}")

    # Hypertables
    cur.execute("SELECT hypertable_name FROM timescaledb_information.hypertables ORDER BY 1")
    hyper = [r[0] for r in cur.fetchall()]
    print(f"\nHypertables: {', '.join(hyper)}")

    # Triggers
    cur.execute("""
        SELECT trigger_name, event_object_table
        FROM information_schema.triggers
        WHERE trigger_schema = 'public'
        ORDER BY trigger_name
    """)
    triggers = cur.fetchall()
    print(f"Triggers: {len(triggers)}")
    for tname, ttable in triggers:
        print(f"  {tname} -> {ttable}")

    cur.close()


def main():
    print("=" * 60)
    print(" CLOUDSTATION PIH - Deploy TimescaleDB v3.1 COMPLETO")
    print(f" {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print("=" * 60)
    print()

    pg = get_pg_config()
    print(f"Conectando a {pg['host']}:{pg['port']}/{pg['database']} ...")

    try:
        conn = psycopg2.connect(
            host=pg["host"],
            port=pg["port"],
            user=pg["user"],
            password=pg["password"],
            database=pg["database"],
        )
        conn.autocommit = False
        print("Conexión OK\n")
    except Exception as e:
        print(f"[ERROR] No se pudo conectar: {e}")
        sys.exit(1)

    try:
        created, skipped = run_deploy(conn)
        verify(conn)

        print(f"\n{'=' * 60}")
        print(f" DEPLOY COMPLETADO")
        print(f" Creados/modificados: {created}")
        print(f" Ya existían: {skipped}")
        print(f" 16 tablas + 2 funciones + 2 triggers")
        print(f"{'=' * 60}")
    except Exception as e:
        conn.rollback()
        print(f"\n[ERROR FATAL] {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)
    finally:
        conn.close()


if __name__ == "__main__":
    main()
