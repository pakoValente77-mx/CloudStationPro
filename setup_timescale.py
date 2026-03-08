import psycopg2
from psycopg2 import sql
import sys
import time

# --- CONFIGURACIÓN ---
# Datos del servidor local (donde se aplica el esquema)
DB_NAME = "mycloud_timescale"
DB_USER = "postgres"
DB_PASS = "Cfe2026##" 
DB_HOST = "192.168.1.72" 
DB_PORT = "5432"

def setup_database_definitive():
    conn = None
    try:
        # 1. Conectar a postgres para reset total
        print(f"[*] Conectando a PostgreSQL en {DB_HOST}...")
        conn = psycopg2.connect(
            dbname="postgres",
            user=DB_USER,
            password=DB_PASS,
            host=DB_HOST,
            port=DB_PORT
        )
        conn.autocommit = True
        cur = conn.cursor()

        print(f"[*] REINICIANDO BASE DE DATOS {DB_NAME} (Wipe & Recreate)...")
        # Cerrar conexiones
        cur.execute(f"""
            SELECT pg_terminate_backend(pg_stat_activity.pid)
            FROM pg_stat_activity
            WHERE pg_stat_activity.datname = '{DB_NAME}'
              AND pid <> pg_backend_pid();
        """)
        cur.execute(f"DROP DATABASE IF EXISTS {DB_NAME}")
        cur.execute(f"CREATE DATABASE {DB_NAME}")
        print(f"[+] Base de datos {DB_NAME} creada limpia.")
        
        cur.close()
        conn.close()

        time.sleep(2)

        # 2. Conectar a la nueva BD
        conn = psycopg2.connect(
            dbname=DB_NAME,
            user=DB_USER,
            password=DB_PASS,
            host=DB_HOST,
            port=DB_PORT
        )
        conn.autocommit = True
        cur = conn.cursor()

        # 3. Extensiones
        print("[*] Habilitando TimescaleDB...")
        cur.execute("CREATE EXTENSION IF NOT EXISTS timescaledb CASCADE;")

        # --- REPLICACIÓN AUTORITATIVA 1:1 ---

        # 4. Tabla bitacora_goes
        print("[*] Creando bitacora_goes...")
        cur.execute("""
            CREATE TABLE public.bitacora_goes (
                id SERIAL PRIMARY KEY,
                dcp_id CHARACTER VARYING(20) NOT NULL,
                timestamp_utc TIMESTAMP WITH TIME ZONE NOT NULL,
                timestamp_msg TIMESTAMP WITH TIME ZONE,
                servidor CHARACTER VARYING(200),
                exito BOOLEAN
            );
        """)

        # 5. Tabla dcp_headers (Con restricción para ON CONFLICT)
        print("[*] Creando dcp_headers...")
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
                CONSTRAINT dcp_headers_pkey PRIMARY KEY (dcp_id, timestamp_msg)
            );
        """)

        # 6. Tabla dcp_datos (Hypertable con restricción para ON CONFLICT)
        print("[*] Creando dcp_datos...")
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
            );
        """)
        cur.execute("SELECT create_hypertable('public.dcp_datos', 'ts');")

        # 7. Tabla resumen_horario
        print("[*] Creando resumen_horario...")
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
            );
        """)
        cur.execute("SELECT create_hypertable('public.resumen_horario', 'ts');")

        # 8. Tabla resumen_diario
        print("[*] Creando resumen_diario...")
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
            );
        """)
        cur.execute("SELECT create_hypertable('public.resumen_diario', 'fecha');")

        # 9. Tabla ultimas_mediciones
        print("[*] Creando ultimas_mediciones...")
        cur.execute("""
            CREATE TABLE public.ultimas_mediciones (
                dcp_id CHARACTER VARYING NOT NULL,
                variable CHARACTER VARYING NOT NULL,
                ts TIMESTAMP WITH TIME ZONE,
                valor REAL,
                tipo CHARACTER VARYING,
                PRIMARY KEY (dcp_id, variable)
            );
        """)

        # 10. Tabla estatus_estaciones
        print("[*] Creando estatus_estaciones...")
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
            );
        """)

        # --- FUNCIONES Y TRIGGERS (IDENTICOS AL ORIGEN) ---
        print("[*] Instalando Triggers y Procedimientos...")

        cur.execute("""
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
        """)

        cur.execute("""
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
        """)

        cur.execute("CREATE TRIGGER trg_update_estatus_on_insert AFTER INSERT ON public.bitacora_goes FOR EACH ROW EXECUTE FUNCTION public.trg_actualizar_estatus();")
        cur.execute("CREATE TRIGGER trg_update_last_val AFTER INSERT ON public.dcp_datos FOR EACH ROW EXECUTE FUNCTION public.trg_actualizar_ultima_medicion();")

        print("\n" + "="*60)
        print(" ESQUEMA COMPLETO Y RESTRICCIONES (PRIMARY KEYS) OK ")
        print("="*60)

    except Exception as e:
        print(f"\n[X] ERROR DURANTE EL SETUP: {e}")
    finally:
        if conn:
            conn.close()

if __name__ == "__main__":
    setup_database_definitive()
