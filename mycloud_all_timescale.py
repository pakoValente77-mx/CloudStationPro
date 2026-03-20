"""
VERSIÓN OPTIMIZADA PARA TIMESCALEDB
- Compresión automática 20:1
- Hypertables (consultas 100x más rápidas)
- Retención automática
- Batch inserts optimizados
"""

import socket
import struct
import hashlib
import time
import os
import schedule
import psycopg2
import psycopg2.pool
from psycopg2.extras import execute_values
import configparser
import datetime
from datetime import timedelta
import pyodbc

# ==================== CONFIGURACIÓN ====================
# Cuando se empaqueta con PyInstaller, el cwd puede ser un directorio temporal.
# Usamos la ubicación real del ejecutable para encontrar config.ini
import sys
if getattr(sys, 'frozen', False):
    _base_dir = os.path.dirname(sys.executable)
else:
    _base_dir = os.path.dirname(os.path.abspath(__file__))

config = configparser.ConfigParser()
config.read(os.path.join(_base_dir, 'config.ini'))

# TimescaleDB (PostgreSQL)
PG_HOST = config.get('timescaledb', 'host', fallback='localhost')
PG_PORT = config.getint('timescaledb', 'port', fallback=5432)
PG_USER = config.get('timescaledb', 'user', fallback='postgres')
PG_PASSWORD = config.get('timescaledb', 'password', fallback='Cfe2026##')
PG_DATABASE = config.get('timescaledb', 'database', fallback='mycloud_timescale')

# ==================== CONFIGURACIÓN SQL SERVER ====================
SQL_MODE = config.get('settings', 'sql_mode', fallback='remoto').lower()

if SQL_MODE == 'remoto':
    sql_section = 'sql_server_remote'
else:
    sql_section = 'sql_server_local'

SQL_SERVER = config.get(sql_section, 'server', fallback='127.0.0.1')
SQL_PORT = config.get(sql_section, 'port', fallback='1433')
SQL_DB = config.get(sql_section, 'database', fallback='IGSCLOUD_PRO')
SQL_USER = config.get(sql_section, 'user', fallback='sa')
SQL_PASSWORD = config.get(sql_section, 'password', fallback='Cfe2026$$')

print(f"[CONFIG] SQL Server Mode: {SQL_MODE.upper()}")
print(f"[CONFIG] Conectando a SQL Server: {SQL_SERVER}:{SQL_PORT}/{SQL_DB}")

SQL_CONN_STR = (
    'DRIVER={ODBC Driver 18 for SQL Server};'
    f'SERVER={SQL_SERVER},{SQL_PORT};'
    f'DATABASE={SQL_DB};'
    f'UID={SQL_USER};'
    f'PWD={SQL_PASSWORD};'
    'TrustServerCertificate=yes;'
    'Encrypt=no;'
)



# LRGS (desde config.ini)
DEFAULT_USER = config.get('lrgs', 'default_user', fallback='cilasur')
DEFAULT_PASSWORD = config.get('lrgs', 'default_password', fallback='ksip-CWNG-05')

HOSTS = [h.strip() for h in config.get('lrgs', 'hosts', fallback='10.41.75.100,lrgseddn1.cr.usgs.gov,lrgseddn2.cr.usgs.gov,lrgseddn3.cr.usgs.gov').split(',')]

# Cargar credenciales especiales por host desde config.ini
CREDENTIALS = {}
for key, value in config.items('lrgs') if config.has_section('lrgs') else []:
    if key.startswith('credentials_'):
        host_name = key[len('credentials_'):]
        parts = value.split(':')
        if len(parts) == 2:
            CREDENTIALS[host_name] = {"user": parts[0], "password": parts[1]}

OUT_DIR = os.path.join(_base_dir, "dds_last")
os.makedirs(OUT_DIR, exist_ok=True)

import platform
if platform.system() == 'Windows':
    MIS_OUT_DIR = config.get('paths', 'mis_output_dir_windows', fallback='C:\\IGSCLOUD\\Datos\\GOES')
else:
    MIS_OUT_DIR = config.get('paths', 'mis_output_dir_mac', fallback='./Datos/GOES')
os.makedirs(MIS_OUT_DIR, exist_ok=True)

# ==================== FUNCIONES AUXILIARES ====================
def generar_archivo_mis(dcp_id, meta, raw_header, raw_payload, valores, estacion_data, sensores_list):
    """
    Genera un archivo .mis con el formato específico solicitado.
    """
    try:
        timestamp = meta['timestamp']
        # Nombre de archivo: DCPID_YYYYMMDDHHMMEX.mis
        filename = f"{dcp_id}_{timestamp.strftime('%Y%m%d%H%M')}EX.mis"
        filepath = os.path.join(MIS_OUT_DIR, filename)
        
        id_asignado = estacion_data.get("id_asignado", "")
        
        # Preparar datos para el cuerpo
        # Reutilizamos lógica de cálculo de valores y timestamps
        lines_body = []
        
        # Asegurar que los sensores estén ordenados por número
        sensores_ordenados = sorted(sensores_list, key=lambda s: s["numero_sensor"])

        for sensor in sensores_ordenados:
            sensor_id_str = sensor["numero_sensor"] # Ya viene con zfill(4) desde la carga
            
            # XML Header por sensor
            lines_body.append(f"<STATION>{id_asignado}</STATION><SENSOR>{sensor_id_str}</SENSOR><DATEFORMAT>YYYYMMDD</DATEFORMAT>")
            
            for i, pos in enumerate(sensor["posiciones"]):
                if pos >= len(valores):
                    continue
                
                # Calcular Valor
                valor_raw = valores[pos]
                valor = round(valor_raw / (10 ** sensor["decimales"]), sensor["decimales"])
                # valor += sensor.get("valor_cota", 0.0)
                
                # Calcular Timestamp
                ts = timestamp - datetime.timedelta(minutes=i * sensor["periodo"])
                ts = hora_concurrente(ts, sensor["periodo"])
                
                # Formato: YYYYMMDD;HH:MM:SS;Valor
                date_str = ts.strftime('%Y%m%d')
                time_str = ts.strftime('%H:%M:%S')
                # Formatear valor: eliminar .0 si es entero, o mostrar decimales si corresponde. 
                # El ejemplo muestra -1310.72 y 300.8 y 0.
                valor_str = f"{valor:g}" 
                
                lines_body.append(f"{date_str};{time_str};{valor_str}")

        with open(filepath, "w", encoding="utf-8") as f:
            # 1. Encabezado (Línea 1)
            # Formato: DCP_ID,DD/MM/YYYY HH:MM:SS,SIGNAL,Good Msg!,CHANNEL
            # Signal: +XXdBm
            sig = meta.get('sig', '00')
            try:
                sig_val = int(sig)
                sig_str = f"+{sig_val}dBm"
            except:
                sig_str = f"{sig}dBm"
            
            channel = str(meta.get('channel', '000')).zfill(3)
            date_header = timestamp.strftime('%d/%m/%Y %H:%M:%S')
            
            header_line = f"{dcp_id},{date_header},{sig_str},Good Msg!,{channel}"
            f.write(header_line + "\n")
            
            # 2. Cuerpo
            for line in lines_body:
                f.write(line + "\n")
            
            # 3. Pie de página (Footer)
            # {<RAW_HEADER><RAW_PAYLOAD>}
            # Raw header son 37 bytes. Raw payload es el resto.
            # Concatenamos bytes y decodificamos a ascii (latin-1 para preservar bytes) o escribimos en modo binario?
            # El archivo original es texto, así que decodificamos 'latin-1' para no perder bytes si hubiera raros,
            # aunque DOMSAT suele ser ASCII.
            
            raw_content = raw_header + raw_payload
            footer_str = raw_content.decode('latin-1', errors='replace')
            f.write(f"{{{footer_str}}}\n")
            
        print(f"[MIS] Generado: {filename}")

    except Exception as e:
        print(f"[ERROR MIS] Falló generación de archivo .mis: {e}")


# Pool de conexiones a TimescaleDB (reutiliza conexiones en lugar de abrir/cerrar cada vez)
_pg_pool = psycopg2.pool.ThreadedConnectionPool(
    minconn=1,
    maxconn=5,
    host=PG_HOST,
    port=PG_PORT,
    user=PG_USER,
    password=PG_PASSWORD,
    database=PG_DATABASE,
    client_encoding='UTF8',
    options='-c client_encoding=UTF8'
)

def get_pg_conn():
    """Obtener conexión del pool de TimescaleDB"""
    return _pg_pool.getconn()

def release_pg_conn(conn):
    """Devolver conexión al pool"""
    if conn:
        try:
            _pg_pool.putconn(conn)
        except Exception:
            pass

def get_sql_conn():
    """Conexión a SQL Server (solo para config)"""
    return pyodbc.connect(SQL_CONN_STR)

def get_credentials(host):
    creds = CREDENTIALS.get(host)
    if creds:
        return creds["user"], creds["password"]
    return DEFAULT_USER, DEFAULT_PASSWORD


# ==================== CREACIÓN AUTOMÁTICA DE TABLAS ====================
_tables_ensured = False

def _ensure_pg_tables():
    """Crea todas las tablas en TimescaleDB si no existen (idempotente)."""
    global _tables_ensured
    if _tables_ensured:
        return
    conn = get_pg_conn()
    try:
        cur = conn.cursor()

        # --- dcp_datos (hypertable) ---
        cur.execute("""
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
        """)
        # Intentar crear hypertable (ignora si ya existe)
        try:
            cur.execute("SELECT create_hypertable('public.dcp_datos', 'ts', if_not_exists => TRUE);")
        except Exception:
            conn.rollback()

        # --- resumen_horario (hypertable) ---
        cur.execute("""
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
        """)
        try:
            cur.execute("SELECT create_hypertable('public.resumen_horario', 'ts', if_not_exists => TRUE);")
        except Exception:
            conn.rollback()

        # --- resumen_diario (hypertable) ---
        cur.execute("""
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
        """)
        try:
            cur.execute("SELECT create_hypertable('public.resumen_diario', 'fecha', if_not_exists => TRUE);")
        except Exception:
            conn.rollback()

        # --- bitacora_goes ---
        cur.execute("""
            CREATE TABLE IF NOT EXISTS public.bitacora_goes (
                id SERIAL PRIMARY KEY,
                dcp_id CHARACTER VARYING(20) NOT NULL,
                timestamp_utc TIMESTAMP WITH TIME ZONE NOT NULL,
                timestamp_msg TIMESTAMP WITH TIME ZONE,
                servidor CHARACTER VARYING(200),
                exito BOOLEAN
            );
        """)

        # --- dcp_headers ---
        cur.execute("""
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
                CONSTRAINT dcp_headers_pkey PRIMARY KEY (dcp_id, timestamp_msg)
            );
        """)

        # --- ultimas_mediciones ---
        cur.execute("""
            CREATE TABLE IF NOT EXISTS public.ultimas_mediciones (
                dcp_id CHARACTER VARYING NOT NULL,
                variable CHARACTER VARYING NOT NULL,
                ts TIMESTAMP WITH TIME ZONE,
                valor REAL,
                tipo CHARACTER VARYING,
                PRIMARY KEY (dcp_id, variable)
            );
        """)

        # --- estatus_estaciones ---
        cur.execute("""
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
        """)

        # --- Trigger para actualizar estatus ---
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

        # Crear trigger solo si no existe
        cur.execute("""
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_trigger WHERE tgname = 'trg_update_estatus_on_insert'
                ) THEN
                    CREATE TRIGGER trg_update_estatus_on_insert
                    AFTER INSERT ON bitacora_goes
                    FOR EACH ROW EXECUTE FUNCTION trg_actualizar_estatus();
                END IF;
            END $$;
        """)

        # --- Trigger para actualizar ultimas_mediciones ---
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

        cur.execute("""
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_trigger WHERE tgname = 'trg_update_last_val'
                ) THEN
                    CREATE TRIGGER trg_update_last_val
                    AFTER INSERT ON dcp_datos
                    FOR EACH ROW EXECUTE FUNCTION trg_actualizar_ultima_medicion();
                END IF;
            END $$;
        """)

        conn.commit()
        cur.close()
        _tables_ensured = True
        print("[PG] ✅ Tablas y triggers verificados/creados")

    except Exception as e:
        print(f"[ERROR] Falló verificación de tablas: {e}")
        try:
            conn.rollback()
        except Exception:
            pass
    finally:
        release_pg_conn(conn)


# ==================== CARGA ESTACIONES ====================
def load_estaciones_from_sql():
    try:
        print("[DB] Conectando a SQL Server para configuración...")
        conn = get_sql_conn()
        cursor = conn.cursor()
        
        query = """
        SELECT [IdSatelital], [Minuto], [Nombre], [IdAsignado], [RangoTransmision]
        FROM [dbo].[NV_GoesSGD]
        WHERE [Minuto] IS NOT NULL 
          AND [Minuto] BETWEEN 0 AND 59
          AND [IdEstacion] IS NOT NULL
          AND [Decodificador] = 'GS300'          
        """
        cursor.execute(query)
        rows = cursor.fetchall()
        
        estaciones = {}
        for row in rows:
            dcp_id = str(row.IdSatelital).strip().upper()
            minuto = int(row.Minuto)
            nombre = str(row.Nombre or "Sin nombre").strip()
            id_asignado = str(row.IdAsignado or "").strip()
            
            # Parsear RangoTransmision de HH:MM:SS a minutos totales
            rango_transmision = 60  # Default: 1 hora
            if row.RangoTransmision:
                try:
                    partes = str(row.RangoTransmision).split(':')
                    if len(partes) == 3:
                        horas = int(partes[0])
                        minutos = int(partes[1])
                        rango_transmision = (horas * 60) + minutos
                except Exception as e:
                    print(f"[WARN] Error parseando RangoTransmision '{row.RangoTransmision}': {e}")
            
            estaciones[dcp_id] = {
                "minuto": minuto,
                "nombre": nombre,
                "id_asignado": id_asignado,
                "rango_transmision": rango_transmision
            }
        
        conn.close()
        print(f"[DB] {len(estaciones)} estaciones GS300 cargadas")
        return estaciones

    except Exception as e:
        print(f"[ERROR] Falló SQL: {e}")
        return {}


# ==================== CARGA CUENCAS / SUBCUENCAS ====================
def load_cuencas_from_sql():
    """Carga mapeo de IdAsignado → (cuenca, subcuenca) desde SQL Server.
    Retorna dict keyed por id_asignado → {'cuenca': str, 'subcuenca': str}."""
    try:
        conn = get_sql_conn()
        cursor = conn.cursor()
        cursor.execute("""
            SELECT e.IdAsignado,
                   ISNULL(c.Nombre, '') AS Cuenca,
                   ISNULL(sc.Nombre, '') AS Subcuenca
            FROM Estacion e
            LEFT JOIN Cuenca c ON e.IdCuenca = c.Id
            LEFT JOIN Subcuenca sc ON e.IdSubcuenca = sc.Id
            WHERE e.Activo = 1
        """)
        cuencas = {}
        for row in cursor.fetchall():
            id_asignado = str(row.IdAsignado).strip()
            cuencas[id_asignado] = {
                "cuenca": str(row.Cuenca).strip(),
                "subcuenca": str(row.Subcuenca).strip()
            }
        conn.close()
        cuencas_unicas = set(v["cuenca"] for v in cuencas.values() if v["cuenca"])
        subcuencas_unicas = set(v["subcuenca"] for v in cuencas.values() if v["subcuenca"])
        print(f"[CUENCAS] {len(cuencas)} estaciones mapeadas | "
              f"{len(cuencas_unicas)} cuencas, {len(subcuencas_unicas)} subcuencas")
        return cuencas
    except Exception as e:
        print(f"[ERROR] Falló carga de cuencas: {e}")
        return {}

# ==================== CARGA SENSORES ====================
def load_sensores_from_sql():
    try:
        conn = get_sql_conn()
        cursor = conn.cursor()
        
        query = """
        SELECT 
            s.IdAsignado,
            s.NumeroSensor,
            s.Sensor,
            s.PuntoDecimal,
            s.PeriodoMuestra,
            s.ValorMinimo,
            s.ValorMaximo,
            s.ValorCota
        FROM [dbo].[NV_SensoresSGD] s             
        """
        cursor.execute(query)
        rows = cursor.fetchall()
        
        sensores_por_estacion = {}
        for row in rows:
            id_asignado = str(row.IdAsignado).strip()
            if id_asignado not in sensores_por_estacion:
                sensores_por_estacion[id_asignado] = []
            valor_cota = row.ValorCota if row.ValorCota is not None else 0.0
            sensores_por_estacion[id_asignado].append({
                "numero_sensor": str(row.NumeroSensor).zfill(4),
                "nombre": str(row.Sensor),
                "decimales": int(row.PuntoDecimal),
                "periodo": int(row.PeriodoMuestra),
                "valor_minimo": float(row.ValorMinimo),
                "valor_maximo": float(row.ValorMaximo),
                "valor_cota": float(valor_cota),
                "posiciones": []
            })
        
        conn.close()
        print(f"[SENSORES] Cargados {sum(len(v) for v in sensores_por_estacion.values())} sensores")
        return sensores_por_estacion

    except Exception as e:
        print(f"[ERROR] Falló carga de sensores: {e}")
        return {}

# ==================== CARGA UMBRALES DE PRECIPITACIÓN ====================
def load_umbrales_precipitacion():
    """Carga umbrales activos de precipitación desde SQL Server (UmbralAlertas).
    Retorna dict keyed por (id_asignado, numero_sensor) → lista de umbrales."""
    try:
        conn = get_sql_conn()
        cursor = conn.cursor()
        cursor.execute("""
            SELECT u.Id, u.ValorReferencia, u.Umbral, u.Operador, u.Periodo,
                   u.Nombre, u.Color,
                   s.NumeroSensor, e.IdAsignado, e.Nombre as EstacionNombre
            FROM UmbralAlertas u
            JOIN Sensor s ON u.IdSensor = s.Id
            JOIN TipoSensor ts ON s.IdTipoSensor = ts.Id
            JOIN Estacion e ON s.IdEstacion = e.Id
            WHERE u.Activo = 1 AND ts.Nombre = N'Precipitación'
        """)
        umbrales = {}
        count = 0
        for row in cursor.fetchall():
            num_sensor = str(row.NumeroSensor).zfill(4)
            id_asignado = str(row.IdAsignado).strip()
            key = (id_asignado, num_sensor)
            if key not in umbrales:
                umbrales[key] = []
            umbrales[key].append({
                "umbral_id": int(row.Id),
                "valor_referencia": float(row.ValorReferencia),
                "umbral": float(row.Umbral),
                "operador": str(row.Operador).strip(),
                "periodo": int(row.Periodo) if row.Periodo else 60,
                "nombre": str(row.Nombre).strip(),
                "color": str(row.Color or "").strip(),
                "estacion_nombre": str(row.EstacionNombre).strip(),
            })
            count += 1
        conn.close()
        print(f"[UMBRALES] {count} umbrales de precipitación cargados ({len(umbrales)} sensores)")
        return umbrales
    except Exception as e:
        print(f"[ERROR] Falló carga de umbrales: {e}")
        return {}


def asignar_posiciones_sensores(sensores_lista, rango_transmision=60):
    """Asignar posiciones secuenciales por periodo
    
    Args:
        sensores_lista: Lista de sensores a procesar
        rango_transmision: Cantidad de minutos de datos que trae cada TX (default 60)
    """
    sensores_ordenados = sorted(sensores_lista, key=lambda s: s["numero_sensor"])
    
    posicion = 0
    for sensor in sensores_ordenados:
        periodo = sensor["periodo"]
        
        if periodo <= 0:
            print(f"[WARN] Periodo inválido ({periodo}) para sensor {sensor['numero_sensor']}. Ignorando.")
            continue
        
        if rango_transmision % periodo != 0:
            print(f"[WARN] Periodo {periodo} no divide rango {rango_transmision}. Ignorando {sensor['numero_sensor']}")
            continue
        
        valores_por_rango = int(rango_transmision // periodo)
        sensor["posiciones"] = list(range(posicion, posicion + valores_por_rango))
        posicion += valores_por_rango
        
        print(f"[POS] {sensor['numero_sensor']} | {sensor['nombre']} | Periodo: {periodo} min | Rango TX: {rango_transmision} min | Posiciones: {sensor['posiciones']}")
    
    return sensores_ordenados

def hora_concurrente(utc_now: datetime.datetime, periodo: int) -> datetime.datetime:
    """Alinear timestamp al múltiplo del periodo"""
    try:
        hora_actual = utc_now.hour
        min_actual = utc_now.minute
        
        if not isinstance(periodo, (int, float)) or periodo <= 0:
            raise ValueError(f"Periodo inválido: {periodo}")
        
        min_alineado = int((min_actual // periodo) * periodo)
        return datetime.datetime(utc_now.year, utc_now.month, utc_now.day, 
                                hora_actual, min_alineado, 0, tzinfo=datetime.timezone.utc)
    except Exception as e:
        print(f"[ERROR hora_concurrente] {e}")
        raise

# ==================== LLUVIA ACUMULADA (24h y periodo actual) ====================
def actualizar_lluvia_acumulada(dcp_id: str, id_asignado: str, sensores: list, conn):
    """
    Calcula precipitación acumulada en dos periodos:
    - '24h':    de 6:00 AM (UTC-6) ayer  a  6:00 AM (UTC-6) hoy
    - 'actual': de 6:00 AM (UTC-6) hoy   a  hora actual
    
    Horario de acumulación: minuto :01 de cada hora a minuto :00 de la siguiente
    (consistente con resumen_horario: ts - INTERVAL '1 minute')
    
    Solo procesa sensores cuyo nombre sea exactamente 'Precipitación'.
    """
    # Filtrar solo sensores de precipitación (exacto, no "precipitación acumulada")
    sensores_precip = [s for s in sensores 
                       if s["nombre"].strip().lower() == 'precipitación']
    if not sensores_precip:
        return
    
    try:
        cur = conn.cursor()
        
        # Crear tabla si no existe
        cur.execute("""
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
        """)
        cur.execute("""
            CREATE INDEX IF NOT EXISTS idx_lluvia_tipo_periodo 
            ON public.lluvia_acumulada (tipo_periodo);
        """)
        conn.commit()
        
        ahora_utc = datetime.datetime.now(datetime.timezone.utc)
        
        # Hora 6:00 AM tiempo local (UTC-6) = 12:00 PM UTC
        hoy_6am_utc = ahora_utc.replace(hour=12, minute=0, second=0, microsecond=0)
        if ahora_utc < hoy_6am_utc:
            # Antes de las 6am local: el "hoy 6am" es ayer a las 12 UTC
            hoy_6am_utc -= datetime.timedelta(days=1)
        
        ayer_6am_utc = hoy_6am_utc - datetime.timedelta(days=1)
        
        for sensor in sensores_precip:
            variable = sensor["nombre"].lower().replace(" ", "_")
            
            # ---- Periodo 24h (ayer 6am a hoy 6am) ----
            cur.execute("""
                SELECT COALESCE(SUM(acumulado), 0), COUNT(*)
                FROM resumen_horario
                WHERE id_asignado = %s AND sensor_id = %s
                  AND ts >= %s AND ts < %s
            """, (id_asignado, sensor["numero_sensor"], ayer_6am_utc, hoy_6am_utc))
            row_24h = cur.fetchone()
            acum_24h = row_24h[0] if row_24h else 0
            horas_24h = row_24h[1] if row_24h else 0
            
            cur.execute("""
                INSERT INTO lluvia_acumulada 
                (id_asignado, dcp_id, sensor_id, variable, 
                 periodo_inicio, periodo_fin, tipo_periodo,
                 acumulado, horas_con_dato, ultima_actualizacion)
                VALUES (%s, %s, %s, %s, %s, %s, '24h', %s, %s, NOW())
                ON CONFLICT (id_asignado, sensor_id, tipo_periodo) DO UPDATE SET
                    dcp_id = EXCLUDED.dcp_id,
                    variable = EXCLUDED.variable,
                    periodo_inicio = EXCLUDED.periodo_inicio,
                    periodo_fin = EXCLUDED.periodo_fin,
                    acumulado = EXCLUDED.acumulado,
                    horas_con_dato = EXCLUDED.horas_con_dato,
                    ultima_actualizacion = NOW();
            """, (id_asignado, dcp_id, sensor["numero_sensor"], variable,
                  ayer_6am_utc, hoy_6am_utc, acum_24h, horas_24h))
            
            # ---- Periodo actual (hoy 6am a ahora) ----
            cur.execute("""
                SELECT COALESCE(SUM(acumulado), 0), COUNT(*)
                FROM resumen_horario
                WHERE id_asignado = %s AND sensor_id = %s
                  AND ts >= %s AND ts < %s
            """, (id_asignado, sensor["numero_sensor"], hoy_6am_utc, ahora_utc))
            row_act = cur.fetchone()
            acum_actual = row_act[0] if row_act else 0
            horas_actual = row_act[1] if row_act else 0
            
            cur.execute("""
                INSERT INTO lluvia_acumulada 
                (id_asignado, dcp_id, sensor_id, variable, 
                 periodo_inicio, periodo_fin, tipo_periodo,
                 acumulado, horas_con_dato, ultima_actualizacion)
                VALUES (%s, %s, %s, %s, %s, %s, 'actual', %s, %s, NOW())
                ON CONFLICT (id_asignado, sensor_id, tipo_periodo) DO UPDATE SET
                    dcp_id = EXCLUDED.dcp_id,
                    variable = EXCLUDED.variable,
                    periodo_inicio = EXCLUDED.periodo_inicio,
                    periodo_fin = EXCLUDED.periodo_fin,
                    acumulado = EXCLUDED.acumulado,
                    horas_con_dato = EXCLUDED.horas_con_dato,
                    ultima_actualizacion = NOW();
            """, (id_asignado, dcp_id, sensor["numero_sensor"], variable,
                  hoy_6am_utc, ahora_utc, acum_actual, horas_actual))
        
        print(f"[LLUVIA] {id_asignado} | 24h: {acum_24h:.1f}mm ({horas_24h}h) | Actual: {acum_actual:.1f}mm ({horas_actual}h)")
        cur.close()
        
    except Exception as e:
        print(f"[ERROR LLUVIA] {id_asignado}: {e}")
        try:
            conn.rollback()
        except Exception:
            pass


# ==================== ALERTAS DE PRECIPITACIÓN ====================
def evaluar_alertas_precipitacion(dcp_id: str, id_asignado: str, sensores: list, conn):
    """
    Evalúa si la precipitación acumulada en el periodo del umbral supera el valor de referencia.
    Lee umbrales de UMBRALES_PRECIP (cargados desde SQL Server).
    Escribe alertas en alertas_precipitacion (TimescaleDB).
    """
    # Filtrar sensores de precipitación
    sensores_precip = [s for s in sensores
                       if s["nombre"].strip().lower() == 'precipitación']
    if not sensores_precip:
        return

    try:
        cur = conn.cursor()

        # Crear tabla si no existe
        cur.execute("""
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
        """)
        cur.execute("""
            CREATE INDEX IF NOT EXISTS idx_alertas_precip_estacion
            ON public.alertas_precipitacion (id_asignado, ts DESC);
        """)
        cur.execute("""
            CREATE INDEX IF NOT EXISTS idx_alertas_precip_activa
            ON public.alertas_precipitacion (activa, notificada);
        """)
        conn.commit()

        ahora_utc = datetime.datetime.now(datetime.timezone.utc)

        for sensor in sensores_precip:
            key = (id_asignado, sensor["numero_sensor"])
            umbrales = UMBRALES_PRECIP.get(key, [])
            if not umbrales:
                continue

            for umb in umbrales:
                periodo = umb["periodo"]  # minutos
                desde = ahora_utc - datetime.timedelta(minutes=periodo)

                # Sumar precipitación en el periodo usando resumen_horario si periodo >= 60,
                # o dcp_datos si periodo < 60
                if periodo >= 60:
                    cur.execute("""
                        SELECT COALESCE(SUM(acumulado), 0)
                        FROM resumen_horario
                        WHERE id_asignado = %s AND sensor_id = %s
                          AND ts >= %s AND ts <= %s
                    """, (id_asignado, sensor["numero_sensor"], desde, ahora_utc))
                else:
                    cur.execute("""
                        SELECT COALESCE(SUM(valor), 0)
                        FROM dcp_datos
                        WHERE id_asignado = %s AND sensor_id = %s
                          AND ts >= %s AND ts <= %s
                          AND valido = true
                    """, (id_asignado, sensor["numero_sensor"], desde, ahora_utc))

                row = cur.fetchone()
                valor_medido = float(row[0]) if row else 0.0

                # Evaluar condición según operador
                limite = umb["valor_referencia"] + umb["umbral"]
                operador = umb["operador"]
                alerta = False

                if operador == '+' and valor_medido >= limite:
                    alerta = True
                elif operador == '-' and valor_medido <= (umb["valor_referencia"] - umb["umbral"]):
                    alerta = True

                if alerta:
                    # Insertar alerta (UPSERT para no duplicar en la misma hora)
                    ts_alerta = ahora_utc.replace(minute=0, second=0, microsecond=0)
                    cur.execute("""
                        INSERT INTO alertas_precipitacion
                        (ts, id_asignado, dcp_id, sensor_id, estacion_nombre,
                         umbral_id, umbral_nombre, valor_referencia, valor_medido,
                         operador, periodo_minutos, color, activa, notificada)
                        VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, TRUE, FALSE)
                        ON CONFLICT (ts, dcp_id, sensor_id, umbral_id) DO UPDATE SET
                            valor_medido = EXCLUDED.valor_medido,
                            activa = TRUE;
                    """, (ts_alerta, id_asignado, dcp_id, sensor["numero_sensor"],
                          umb["estacion_nombre"], umb["umbral_id"], umb["nombre"],
                          umb["valor_referencia"], valor_medido, operador,
                          periodo, umb["color"]))

                    print(f"[ALERTA] {id_asignado} | {umb['estacion_nombre']} | "
                          f"{umb['nombre']} | Medido: {valor_medido:.1f}mm >= {limite:.1f}mm")

        cur.close()

    except Exception as e:
        print(f"[ERROR ALERTAS] {id_asignado}: {e}")
        try:
            conn.rollback()
        except Exception:
            pass


# ==================== EVENTOS DE LLUVIA ====================
def _detectar_sospechoso(valores_batch, periodo, acumulado_evento, duracion_evento_min):
    """
    Validación exhaustiva multi-criterio para detectar datos de precipitación sospechosos.
    Retorna (es_sospechoso: bool, motivos: list[str]).

    Criterios:
    1. Lectura individual extrema (>10mm en un periodo)
    2. Intensidad instantánea absurda (>100 mm/h)
    3. Pico abrupto: lectura >20× la mediana del lote
    4. Sensor pegado / ruido constante: >70% lecturas no-cero con misma resolución mínima
    5. Evento prolongado sin pausas naturales (>60 min, 0 ceros, promedio <0.3mm)
    6. Acumulado alto por goteo: promedio <0.25mm/lectura pero total >5mm
    """
    motivos = []
    if not valores_batch:
        return False, motivos

    vals = [float(v) for v in valores_batch]
    no_cero = [v for v in vals if v > 0]

    # --- 1. Lectura individual extrema ---
    for v in vals:
        if v >= 10.0:
            motivos.append(f"Lectura extrema {v:.1f}mm en {periodo}min (umbral 10mm)")

    # --- 2. Intensidad instantánea absurda ---
    for v in vals:
        intensidad = v * (60.0 / periodo)
        if intensidad >= 100.0:
            motivos.append(f"Intensidad {intensidad:.1f}mm/h (umbral 100mm/h)")

    # --- 3. Pico abrupto respecto al lote ---
    if len(no_cero) >= 3:
        sorted_nz = sorted(no_cero)
        mediana = sorted_nz[len(sorted_nz) // 2]
        maximo = max(no_cero)
        if mediana > 0 and maximo >= 20.0 * mediana:
            motivos.append(f"Pico abrupto: max {maximo:.1f}mm vs mediana {mediana:.2f}mm "
                           f"(ratio {maximo/mediana:.0f}×)")

    # --- 4. Sensor pegado / ruido constante ---
    if len(vals) >= 5:
        pct_no_cero = len(no_cero) / len(vals)
        if pct_no_cero >= 0.70 and no_cero:
            # Verificar si la mayoría de lecturas son el mismo valor (resolución mínima)
            from collections import Counter
            conteo = Counter(round(v, 2) for v in no_cero)
            val_freq, freq = conteo.most_common(1)[0]
            if freq / len(no_cero) >= 0.50 and val_freq <= 0.3:
                motivos.append(f"Sensor pegado/ruido: {freq}/{len(no_cero)} lecturas "
                               f"en {val_freq}mm ({pct_no_cero*100:.0f}% no-cero)")

    # --- 5. Evento prolongado sin pausas naturales ---
    if duracion_evento_min >= 60:
        ceros_en_batch = sum(1 for v in vals if v == 0)
        if ceros_en_batch == 0 and no_cero:
            promedio = sum(no_cero) / len(no_cero)
            if promedio < 0.3:
                motivos.append(f"Sin pausas >60min con promedio {promedio:.2f}mm/lectura "
                               f"(patrón de ruido)")

    # --- 6. Acumulado alto por goteo constante ---
    if no_cero and acumulado_evento >= 5.0:
        total_lecturas_estimadas = max(duracion_evento_min / periodo, len(vals))
        promedio_por_lectura = acumulado_evento / total_lecturas_estimadas if total_lecturas_estimadas > 0 else 0
        if promedio_por_lectura < 0.25 and promedio_por_lectura > 0:
            motivos.append(f"Acumulado {acumulado_evento:.1f}mm por goteo "
                           f"(promedio {promedio_por_lectura:.2f}mm/lectura, patrón de sensor con fuga)")

    # Deduplicar motivos similares
    motivos_unicos = list(dict.fromkeys(motivos))
    return len(motivos_unicos) > 0, motivos_unicos


def actualizar_eventos_lluvia(dcp_id: str, id_asignado: str, sensores: list, conn,
                              timestamp_goes: datetime.datetime, rango_tx: int):
    """
    Detecta y sigue eventos de lluvia continua por estación/sensor.
    - INICIO: primer valor > 0 después de estar en 0
    - FIN: 2 valores consecutivos en 0
    - Almacena: inicio, fin, acumulado_mm, intensidad_max_mmh, duracion_minutos
    - Validación exhaustiva multi-criterio para marcar datos sospechosos
    Usa ultimo_ts_procesado para no reprocesar datos.
    """
    sensores_precip = [s for s in sensores
                       if s["nombre"].strip().lower() == 'precipitación']
    if not sensores_precip:
        return

    try:
        cur = conn.cursor()

        # Crear tabla si no existe
        cur.execute("""
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
        """)
        # Migrar tabla existente: agregar columnas si faltan
        cur.execute("""
            DO $$ BEGIN
                ALTER TABLE public.eventos_lluvia ADD COLUMN IF NOT EXISTS sospechoso BOOLEAN DEFAULT FALSE;
                ALTER TABLE public.eventos_lluvia ADD COLUMN IF NOT EXISTS motivo_sospecha TEXT;
            END $$;
        """)
        cur.execute("""
            CREATE INDEX IF NOT EXISTS idx_eventos_lluvia_activo
            ON public.eventos_lluvia (id_asignado, sensor_id, estado);
        """)
        cur.execute("""
            CREATE INDEX IF NOT EXISTS idx_eventos_lluvia_inicio
            ON public.eventos_lluvia (inicio DESC);
        """)
        conn.commit()

        nombre_est = ESTACIONES.get(dcp_id, {}).get("nombre", "")

        for sensor in sensores_precip:
            periodo = sensor["periodo"]  # típicamente 10 min
            sensor_id = sensor["numero_sensor"]

            # Buscar evento activo para este sensor
            cur.execute("""
                SELECT id, acumulado_mm, intensidad_max_mmh, ceros_consecutivos,
                       inicio, ultimo_ts_procesado
                FROM eventos_lluvia
                WHERE id_asignado = %s AND sensor_id = %s AND estado = 'activo'
                ORDER BY inicio DESC LIMIT 1
            """, (id_asignado, sensor_id))
            evento = cur.fetchone()

            # Determinar desde dónde leer datos nuevos
            if evento and evento[5]:  # tiene ultimo_ts_procesado
                desde = evento[5]
            else:
                desde = timestamp_goes - datetime.timedelta(minutes=rango_tx)

            # Obtener datos nuevos cronológicamente
            cur.execute("""
                SELECT ts, valor FROM dcp_datos
                WHERE id_asignado = %s AND sensor_id = %s
                  AND ts > %s AND ts <= %s
                  AND valido = true
                ORDER BY ts ASC
            """, (id_asignado, sensor_id, desde, timestamp_goes))
            datos = cur.fetchall()
            if not datos:
                continue

            # Estado actual del evento
            evento_id = evento[0] if evento else None
            acumulado = float(evento[1]) if evento else 0.0
            intensidad_max = float(evento[2]) if evento else 0.0
            ceros = int(evento[3]) if evento else 0
            inicio = evento[4] if evento else None

            # Colectar valores del lote para análisis posterior
            valores_batch = []

            for ts_dato, valor in datos:
                valor = float(valor)
                valores_batch.append(valor)

                if valor > 0:
                    intensidad = valor * (60.0 / periodo)  # mm/h
                    ceros = 0

                    if evento_id is None:
                        # NUEVO EVENTO: inicio de lluvia
                        cur.execute("""
                            INSERT INTO eventos_lluvia
                            (id_asignado, dcp_id, sensor_id, estacion_nombre,
                             inicio, acumulado_mm, intensidad_max_mmh,
                             estado, ceros_consecutivos, ultimo_ts_procesado)
                            VALUES (%s, %s, %s, %s, %s, %s, %s, 'activo', 0, %s)
                            RETURNING id
                        """, (id_asignado, dcp_id, sensor_id, nombre_est,
                              ts_dato, valor, intensidad, ts_dato))
                        evento_id = cur.fetchone()[0]
                        acumulado = valor
                        intensidad_max = intensidad
                        inicio = ts_dato
                        print(f"[EVENTO] \U0001f327\ufe0f INICIO lluvia {id_asignado} | "
                              f"{nombre_est} | {ts_dato}")
                    else:
                        # CONTINÚA: acumular
                        acumulado += valor
                        intensidad_max = max(intensidad_max, intensidad)
                else:
                    # valor == 0
                    if evento_id is not None:
                        ceros += 1

            # Actualizar estado final después de procesar todos los datos
            ultimo_ts = datos[-1][0]

            if evento_id is not None:
                duracion = int((ultimo_ts - inicio).total_seconds() / 60)

                # === VALIDACIÓN EXHAUSTIVA MULTI-CRITERIO ===
                sospechoso, motivos = _detectar_sospechoso(
                    valores_batch, periodo, acumulado, duracion)

                motivo_txt = '; '.join(motivos) if motivos else None
                marca_sosp = '⚠️  SOSPECHOSO ' if sospechoso else ''

                if sospechoso:
                    for m in motivos:
                        print(f"[EVENTO] ⚠️  SOSPECHOSO {id_asignado} | {nombre_est} | {m}")

                if ceros >= 2:
                    # CERRAR EVENTO: 2 ceros consecutivos
                    cur.execute("""
                        UPDATE eventos_lluvia SET
                            fin = %s, acumulado_mm = %s, intensidad_max_mmh = %s,
                            duracion_minutos = %s, estado = 'finalizado',
                            sospechoso = sospechoso OR %s,
                            motivo_sospecha = COALESCE(motivo_sospecha, %s),
                            ceros_consecutivos = %s, ultimo_ts_procesado = %s,
                            ultima_actualizacion = NOW()
                        WHERE id = %s
                    """, (ultimo_ts, acumulado, intensidad_max, duracion,
                          sospechoso, motivo_txt,
                          ceros, ultimo_ts, evento_id))
                    print(f"[EVENTO] \u2705 {marca_sosp}FIN lluvia {id_asignado} | {nombre_est} | "
                          f"Acum: {acumulado:.1f}mm | Duración: {duracion}min | "
                          f"Int.max: {intensidad_max:.1f}mm/h")
                else:
                    # ACTUALIZAR evento activo
                    cur.execute("""
                        UPDATE eventos_lluvia SET
                            acumulado_mm = %s, intensidad_max_mmh = %s,
                            duracion_minutos = %s,
                            sospechoso = sospechoso OR %s,
                            motivo_sospecha = COALESCE(motivo_sospecha, %s),
                            ceros_consecutivos = %s,
                            ultimo_ts_procesado = %s, ultima_actualizacion = NOW()
                        WHERE id = %s
                    """, (acumulado, intensidad_max, duracion,
                          sospechoso, motivo_txt,
                          ceros, ultimo_ts, evento_id))
                    if acumulado > 0:
                        print(f"[EVENTO] \U0001f327\ufe0f {marca_sosp}ACTIVO {id_asignado} | {nombre_est} | "
                              f"Acum: {acumulado:.1f}mm | {duracion}min | "
                              f"Int.max: {intensidad_max:.1f}mm/h")

        cur.close()

    except Exception as e:
        print(f"[ERROR EVENTOS] {id_asignado}: {e}")
        try:
            conn.rollback()
        except Exception:
            pass


# ==================== PRECIPITACIÓN PROMEDIO POR CUENCA ====================
def actualizar_precipitacion_cuenca(conn):
    """Calcula precipitación promedio de la última hora por cuenca y subcuenca.
    Semaforiza según intensidad: verde < 2.5, amarillo < 7.5, naranja < 15, rojo >= 15 mm/h."""
    if not CUENCAS_ESTACION:
        return

    try:
        cur = conn.cursor()

        # Crear tabla si no existe
        cur.execute("""
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
        """)
        cur.execute("""
            CREATE INDEX IF NOT EXISTS idx_precip_cuenca_ts
            ON public.precipitacion_cuenca (ts DESC, tipo);
        """)
        conn.commit()

        ahora_utc = datetime.datetime.now(datetime.timezone.utc)
        ts_hora = ahora_utc.replace(minute=0, second=0, microsecond=0)
        desde = ts_hora - datetime.timedelta(hours=1)

        # Obtener precipitación acumulada de la última hora por estación
        # (resumen_horario.ts = inicio de la hora, acumulado = lluvia de esa hora)
        cur.execute("""
            SELECT id_asignado, COALESCE(SUM(acumulado), 0) AS precip_mm
            FROM resumen_horario
            WHERE variable = 'precipitación'
              AND ts >= %s AND ts < %s
            GROUP BY id_asignado
        """, (desde, ts_hora))
        datos_estacion = {row[0]: float(row[1]) for row in cur.fetchall()}

        # Agrupar por cuenca y subcuenca
        cuencas_agg = {}   # nombre_cuenca → {valores:[], total:int}
        sub_agg = {}       # nombre_subcuenca → {valores:[], total:int}

        # Contar estaciones totales por cuenca/subcuenca
        for id_asignado, info in CUENCAS_ESTACION.items():
            cuenca = info["cuenca"]
            subcuenca = info["subcuenca"]
            if cuenca:
                if cuenca not in cuencas_agg:
                    cuencas_agg[cuenca] = {"valores": [], "total": 0}
                cuencas_agg[cuenca]["total"] += 1
            if subcuenca:
                if subcuenca not in sub_agg:
                    sub_agg[subcuenca] = {"valores": [], "total": 0}
                sub_agg[subcuenca]["total"] += 1

        # Asignar valores de precipitación
        for id_asignado, precip in datos_estacion.items():
            info = CUENCAS_ESTACION.get(id_asignado)
            if not info:
                continue
            if info["cuenca"] and info["cuenca"] in cuencas_agg:
                cuencas_agg[info["cuenca"]]["valores"].append(precip)
            if info["subcuenca"] and info["subcuenca"] in sub_agg:
                sub_agg[info["subcuenca"]]["valores"].append(precip)

        def semaforo(promedio):
            if promedio < 2.5:
                return "verde"
            elif promedio < 7.5:
                return "amarillo"
            elif promedio < 15.0:
                return "naranja"
            else:
                return "rojo"

        # Insertar/actualizar por cuenca y subcuenca
        registros = []
        for nombre, agg in cuencas_agg.items():
            vals = agg["valores"]
            prom = sum(vals) / len(vals) if vals else 0.0
            registros.append((ts_hora, "cuenca", nombre, prom,
                              max(vals) if vals else 0.0,
                              min(vals) if vals else 0.0,
                              len(vals), agg["total"], semaforo(prom)))

        for nombre, agg in sub_agg.items():
            vals = agg["valores"]
            prom = sum(vals) / len(vals) if vals else 0.0
            registros.append((ts_hora, "subcuenca", nombre, prom,
                              max(vals) if vals else 0.0,
                              min(vals) if vals else 0.0,
                              len(vals), agg["total"], semaforo(prom)))

        for reg in registros:
            cur.execute("""
                INSERT INTO precipitacion_cuenca
                (ts, tipo, nombre, promedio_mm, max_mm, min_mm,
                 estaciones_con_dato, estaciones_total, semaforo, ultima_actualizacion)
                VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, NOW())
                ON CONFLICT (ts, tipo, nombre) DO UPDATE SET
                    promedio_mm = EXCLUDED.promedio_mm,
                    max_mm = EXCLUDED.max_mm,
                    min_mm = EXCLUDED.min_mm,
                    estaciones_con_dato = EXCLUDED.estaciones_con_dato,
                    estaciones_total = EXCLUDED.estaciones_total,
                    semaforo = EXCLUDED.semaforo,
                    ultima_actualizacion = NOW();
            """, reg)

        # Log resumen
        cuencas_con_lluvia = [(n, a) for n, a in cuencas_agg.items() if a["valores"] and sum(a["valores"]) > 0]
        for nombre, agg in sorted(cuencas_con_lluvia, key=lambda x: -(sum(x[1]["valores"])/len(x[1]["valores"]))):
            vals = agg["valores"]
            prom = sum(vals) / len(vals)
            print(f"[CUENCA] {nombre} | Prom: {prom:.1f}mm/h | Max: {max(vals):.1f}mm | "
                  f"{len(vals)}/{agg['total']} estaciones | Semáforo: {semaforo(prom).upper()}")

        sub_con_lluvia = [(n, a) for n, a in sub_agg.items() if a["valores"] and sum(a["valores"]) > 0]
        for nombre, agg in sorted(sub_con_lluvia, key=lambda x: -(sum(x[1]["valores"])/len(x[1]["valores"]))):
            vals = agg["valores"]
            prom = sum(vals) / len(vals)
            print(f"[SUBCUENCA] {nombre} | Prom: {prom:.1f}mm/h | Max: {max(vals):.1f}mm | "
                  f"{len(vals)}/{agg['total']} estaciones | Semáforo: {semaforo(prom).upper()}")

        if not cuencas_con_lluvia and not sub_con_lluvia:
            print(f"[CUENCA] Sin precipitación en la última hora ({desde} → {ts_hora})")

        cur.close()

    except Exception as e:
        print(f"[ERROR CUENCA] {e}")
        try:
            conn.rollback()
        except Exception:
            pass


# ==================== INSERCIÓN OPTIMIZADA TIMESCALEDB ====================
def insertar_datos_mapeados(dcp_id: str, id_asignado: str, timestamp_goes: datetime.datetime, valores: list):
    print(f"[DEBUG] timestamp_goes (antes de insertar): {timestamp_goes} (tzinfo={getattr(timestamp_goes, 'tzinfo', None)})")
    """
    VERSIÓN OPTIMIZADA PARA TIMESCALEDB
    - Usa execute_values (batch insert 10x más rápido)
    - Inserta en hypertable con compresión automática
    - Actualiza resúmenes con UPSERT
    """
    sensores = SENSORES.get(id_asignado)
    if not sensores:
        return
    
    conn = get_pg_conn()
    try:
        cur = conn.cursor()
    
        # ==================== PREPARAR BATCH DE DATOS CRUDOS ====================
        registros_dict = {}
        for sensor in sensores:
            for i, pos in enumerate(sensor["posiciones"]):
                if pos >= len(valores):
                    continue
                valor_raw = valores[pos]
                valor = round(valor_raw / (10 ** sensor["decimales"]), sensor["decimales"])
                # valor += sensor.get("valor_cota", 0.0)
                ts = timestamp_goes - datetime.timedelta(minutes=i * sensor["periodo"])
                ts = hora_concurrente(ts, sensor["periodo"])
                valido = True
                descripcion = "OK"
                if "valor_minimo" in sensor and valor < sensor["valor_minimo"]:
                    valido = False
                    descripcion = f"Bajo: {valor} < {sensor['valor_minimo']}"
                elif "valor_maximo" in sensor and valor > sensor["valor_maximo"]:
                    valido = False
                    descripcion = f"Alto: {valor} > {sensor['valor_maximo']}"
                key = (ts, dcp_id, sensor["numero_sensor"])
                registros_dict[key] = (
                    ts,
                    dcp_id,
                    id_asignado,
                    sensor["numero_sensor"],
                    sensor["nombre"].lower().replace(" ", "_"),
                    valor,
                    sensor.get("tipo", sensor["nombre"]).lower(),
                    valido,
                    descripcion[:200],
                    ts.year,
                    ts.month,
                    ts.day,
                    ts.hour,
                    ts.minute
                )
        registros_batch = list(registros_dict.values())
        
        # ==================== INSERCIÓN MASIVA (10x más rápida) ====================
        if registros_batch:
            execute_values(cur, """
                INSERT INTO dcp_datos 
                (ts, dcp_id, id_asignado, sensor_id, variable, valor, tipo, 
                 valido, descripcion, anio, mes, dia, hora, minuto)
                VALUES %s
                ON CONFLICT (ts, dcp_id, sensor_id) DO UPDATE SET
                    id_asignado = EXCLUDED.id_asignado,
                    variable = EXCLUDED.variable,
                    valor = EXCLUDED.valor,
                    tipo = EXCLUDED.tipo,
                    valido = EXCLUDED.valido,
                    descripcion = EXCLUDED.descripcion,
                    anio = EXCLUDED.anio,
                    mes = EXCLUDED.mes,
                    dia = EXCLUDED.dia,
                    hora = EXCLUDED.hora,
                    minuto = EXCLUDED.minuto
            """, registros_batch, page_size=500)
            
            conn.commit()
            print(f"[PG] ✅ {len(registros_batch)} registros insertados en hypertable")
        
        # ==================== RESÚMENES HORARIOS ====================
        estacion_config = ESTACIONES.get(dcp_id, {})
        rango_tx = estacion_config.get("rango_transmision", 60)
        
        ts_inicio = timestamp_goes - datetime.timedelta(minutes=rango_tx)
        ts_fin = timestamp_goes
        
        ts_inicio = hora_concurrente(ts_inicio, 60)

        for sensor in sensores:
            try:
                cur.execute("""
                    INSERT INTO resumen_horario 
                    (ts, dcp_id, sensor_id, id_asignado, variable, tipo,
                     suma, conteo, promedio, minimo, maximo, acumulado)
                    SELECT 
                        date_trunc('hour', ts - INTERVAL '1 minute') AS hora,
                        dcp_id,
                        sensor_id,
                        id_asignado,
                        variable,
                        tipo,
                        SUM(valor),
                        COUNT(*),
                        AVG(valor),
                        MIN(valor),
                        MAX(valor),
                        SUM(valor)
                    FROM dcp_datos
                    WHERE id_asignado = %s AND sensor_id = %s
                      AND ts > %s AND ts <= %s
                      AND valido = true
                    GROUP BY date_trunc('hour', ts - INTERVAL '1 minute'), dcp_id, sensor_id, id_asignado, variable, tipo
                    ON CONFLICT (ts, dcp_id, sensor_id) DO UPDATE SET
                        suma = EXCLUDED.suma,
                        conteo = EXCLUDED.conteo,
                        promedio = EXCLUDED.promedio,
                        minimo = EXCLUDED.minimo,
                        maximo = EXCLUDED.maximo,
                        acumulado = EXCLUDED.acumulado;
                """, (id_asignado, sensor["numero_sensor"], ts_inicio, ts_fin))
                
            except Exception as e:
                print(f"[WARN] Resumen horario falló: {e}")
        
        # ==================== RESÚMENES DIARIOS ====================
        fecha_inicio = (ts_inicio - datetime.timedelta(minutes=1)).date()
        fecha_fin = (ts_fin - datetime.timedelta(minutes=1)).date()
        
        fechas_afectadas = set()
        fecha_cursor = fecha_inicio
        while fecha_cursor <= fecha_fin:
            fechas_afectadas.add(fecha_cursor)
            fecha_cursor += datetime.timedelta(days=1)
        
        print(f"[DIARIO] Fechas a recalcular: {fechas_afectadas}")
        
        for fecha_resumen in fechas_afectadas:
            for sensor in sensores:
                try:
                    cur.execute("""
                        INSERT INTO resumen_diario 
                        (fecha, dcp_id, sensor_id, id_asignado, variable, tipo,
                         suma, conteo, promedio, minimo, maximo, acumulado)
                        SELECT 
                            DATE(ts - INTERVAL '1 minute'),
                            dcp_id,
                            sensor_id,
                            id_asignado,
                            variable,
                            tipo,
                            SUM(valor),
                            COUNT(*),
                            AVG(valor),
                            MIN(valor),
                            MAX(valor),
                            SUM(valor)
                        FROM dcp_datos
                        WHERE id_asignado = %s AND sensor_id = %s
                          AND DATE(ts - INTERVAL '1 minute') = %s
                          AND valido = true
                        GROUP BY DATE(ts - INTERVAL '1 minute'), dcp_id, sensor_id, id_asignado, variable, tipo
                        ON CONFLICT (fecha, dcp_id, sensor_id) DO UPDATE SET
                            suma = EXCLUDED.suma,
                            conteo = EXCLUDED.conteo,
                            promedio = EXCLUDED.promedio,
                            minimo = EXCLUDED.minimo,
                            maximo = EXCLUDED.maximo,
                            acumulado = EXCLUDED.acumulado;
                    """, (id_asignado, sensor["numero_sensor"], fecha_resumen))
                    
                except Exception as e:
                    print(f"[WARN] Resumen diario falló para {fecha_resumen}: {e}")
        
        conn.commit()
        print("[PG] ✅ Resúmenes horario y diario actualizados")
        
        # ==================== LLUVIA ACUMULADA ====================
        actualizar_lluvia_acumulada(dcp_id, id_asignado, sensores, conn)
        conn.commit()

        # ==================== ALERTAS DE PRECIPITACIÓN ====================
        evaluar_alertas_precipitacion(dcp_id, id_asignado, sensores, conn)
        conn.commit()

        # ==================== EVENTOS DE LLUVIA ====================
        actualizar_eventos_lluvia(dcp_id, id_asignado, sensores, conn,
                                  timestamp_goes, rango_tx)
        conn.commit()

        # ==================== PRECIPITACIÓN POR CUENCA ====================
        actualizar_precipitacion_cuenca(conn)
        conn.commit()
        cur.close()

    except Exception as e:
        print(f"[ERROR insertar_datos_mapeados] {e}")
        try:
            conn.rollback()
        except Exception:
            pass
    finally:
        release_pg_conn(conn)

# ==================== LOG BITÁCORA ====================
def log_to_pg(dcp_id: str, success: bool, timestamp: datetime.datetime | None, host: str | None):
    """Registrar en TimescaleDB"""
    conn = None
    try:
        conn = get_pg_conn()
        cur = conn.cursor()
        
        ts_utc = datetime.datetime.now(datetime.timezone.utc)
        
        cur.execute("""
            INSERT INTO bitacora_goes 
            (dcp_id, timestamp_utc, timestamp_msg, servidor, exito)
            VALUES (%s, %s, %s, %s, %s)
        """, (dcp_id, ts_utc, timestamp, host, success))
        
        conn.commit()
        cur.close()
        
        print(f"[LOG] {dcp_id} {'✅' if success else '❌'}")
        
    except Exception as e:
        print(f"[ERROR LOG] {e}")
    finally:
        release_pg_conn(conn)

def guardar_header_goes(dcp_id: str, meta: dict):
    """Guardar datos del header GOES en TimescaleDB"""
    conn = None
    try:
        conn = get_pg_conn()
        cur = conn.cursor()
        
        # Signal strength es ASCII directo (ej: "39" = 39)
        signal_strength = None
        try:
            if meta.get("sig"):
                signal_strength = int(meta["sig"])
        except:
            pass
        
        cur.execute("""
            INSERT INTO dcp_headers 
            (timestamp_msg, ts, dcp_id, signal_strength, frequency_offset, mod_index,
             data_quality, channel, spacecraft, data_source, failure_code)
            VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
            ON CONFLICT (dcp_id, timestamp_msg) DO NOTHING
        """, (
            meta["timestamp"],
            meta["timestamp"],
            dcp_id,
            signal_strength,
            meta.get("freq"),
            meta.get("mod"),
            meta.get("qual"),
            meta.get("channel"),
            meta.get("spacecraft"),
            meta.get("uplink"),
            meta.get("fail")
        ))
        
        conn.commit()
        cur.close()
        
        if signal_strength:
            print(f"[HEADER] {dcp_id} | Señal: {signal_strength} | Calidad: {meta.get('qual')}")
        
    except Exception as e:
        print(f"[ERROR HEADER] {e}")
    finally:
        release_pg_conn(conn)

# ==================== DECODIFICACIÓN (SIN CAMBIOS) ====================
def ConvertFrom6BitsSutron(binaryDataMsg):
    try:
        dataMsgIn6Bits = binaryDataMsg.decode('ascii')
        valueIn2Base = ''
        result = []
        sub = 64

        for by in dataMsgIn6Bits:
            by = ord(by)
            val = by if by < 64 else by - sub
            r = bin(val)[2:].zfill(6)
            valueIn2Base += r

        while len(valueIn2Base) >= 18:
            base6 = valueIn2Base[:18]
            valueIn2Base = valueIn2Base[18:]
            # Decodifica como entero con signo (complemento a dos, 18 bits)
            if base6[0] == '1':
                val = int(base6, 2) - (1 << 18)
            else:
                val = int(base6, 2)
            result.append(val)

        return result
    except Exception as e:
        print(f"[DECODE] Error: {e}")
        return []

# ==================== DDS PROTOCOL (SIN CAMBIOS) ====================
def compute_auth_hello_body(username: str, password: str) -> bytes:
    now_utc = datetime.datetime.now(datetime.timezone.utc).replace(second=0, microsecond=0)
    doy = now_utc.timetuple().tm_yday
    tstamp = f"{now_utc.year % 100:02d}{doy:03d}{now_utc.hour:02d}{now_utc.minute:02d}{now_utc.second:02d}"
    
    b_name = username.encode("ascii")
    b_passwd = password.encode("ascii")
    
    ms1 = bytearray()
    ms1.extend(b_name)
    ms1.extend(b_passwd)
    ms1.extend(b_name)
    ms1.extend(b_passwd)
    
    b_sha_password = hashlib.sha1(ms1).digest()
    
    dt_epoch = datetime.datetime(1970, 1, 1, tzinfo=datetime.timezone.utc)
    timet = int((now_utc - dt_epoch).total_seconds())
    b_timet = struct.pack(">I", timet)
    
    ms2 = bytearray()
    ms2.extend(b_name)
    ms2.extend(b_sha_password)
    ms2.extend(b_timet)
    ms2.extend(b_name)
    ms2.extend(b_sha_password)
    ms2.extend(b_timet)
    
    b_final_sha_password = hashlib.sha1(ms2).digest()
    auth_hex = b_final_sha_password.hex().upper()
    body_str = f"{username} {tstamp} {auth_hex} 8"
    
    return body_str.encode("ascii")

def dds_send(sock: socket.socket, type_code: str, body: bytes) -> None:
    header = b"FAF0" + type_code.encode("ascii") + f"{len(body):05d}".encode("ascii")
    sock.sendall(header + body)

def dds_recv(sock: socket.socket, timeout: float = 30.0):
    sock.settimeout(timeout)
    hdr = b""
    while len(hdr) < 10:
        chunk = sock.recv(10 - len(hdr))
        if not chunk:
            raise ConnectionError("Conexión cerrada")
        hdr += chunk
    if hdr[:4] != b"FAF0":
        raise ValueError(f"Encabezado inválido: {hdr[:4]}")
    type_code = hdr[4:5].decode("ascii")
    length = int(hdr[5:10].decode("ascii"))
    body = b""
    while len(body) < length:
        chunk = sock.recv(length - len(body))
        if not chunk:
            raise ConnectionError("Cuerpo incompleto")
        body += chunk
    return type_code, body

def parse_iddcp_response(body: bytes):
    if len(body) < 77:
        return None, None, None
    filename = body[:40]
    header = body[40:77]
    payload = body[77:]
    return filename, header, payload

def decode_domsat_header(header: bytes):
    dcp = header[0:8].decode("ascii", errors="replace")
    tstr = header[8:19].decode("ascii", errors="replace")
    yy = int(tstr[0:2]); ddd = int(tstr[2:5]); hh = int(tstr[5:7]); mm = int(tstr[7:9]); ss = int(tstr[9:11])
    year = 2000 + yy
    base = datetime.datetime(year, 1, 1, tzinfo=datetime.timezone.utc) + timedelta(days=ddd - 1)
    ts = base.replace(hour=hh, minute=mm, second=ss)
    return {
        "dcp": dcp,
        "timestamp": ts,
        "tstr": tstr,
        "type": header[19:20].decode("ascii", errors="replace"),
        "sig": header[20:22].decode("ascii", errors="replace"),
        "freq": header[22:24].decode("ascii", errors="replace"),
        "mod": header[24:25].decode("ascii", errors="replace"),
        "qual": header[25:26].decode("ascii", errors="replace"),
        "channel": header[26:29].decode("ascii", errors="replace"),
        "spacecraft": header[29:30].decode("ascii", errors="replace"),
        "uplink": header[30:32].decode("ascii", errors="replace"),
        "mlen": int(header[32:37].decode("ascii", errors="replace")),
    }

def build_criteria_text(dcp_id: str, since: str = "now - 2 hours", until: str = "now", retransmitted: str = "N") -> str:
    lines = ["#", "# LRGS Search Criteria", "#"]
    lines.append(f"DRS_SINCE: {since}")
    lines.append(f"DRS_UNTIL: {until}")
    lines.append(f"DCP_ADDRESS: {dcp_id}")
    lines.append(f"RETRANSMITTED: {retransmitted}")
    return "\n".join(lines) + "\n"

# ==================== DESCARGA ====================
def fetch_from_host(host: str, dcp_id: str, nombre: str) -> bool:
    print(f"[INFO] {host} -> {dcp_id} | {nombre}")
    try:
        user, password = get_credentials(host)

        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
            sock.settimeout(20)
            sock.connect((host, 16003))

            dds_send(sock, "m", compute_auth_hello_body(user, password))
            t, _ = dds_recv(sock)
            if t != "m": return False

            criteria_text = build_criteria_text(dcp_id)
            body = (b" " * 50) + criteria_text.encode("ascii")
            dds_send(sock, "g", body)
            t, _ = dds_recv(sock)
            if t != "g": return False

            dds_send(sock, "f", b"")
            t, resp = dds_recv(sock)
            if t != "f": return False

            filename, header, payload = parse_iddcp_response(resp)
            if not header: return False

            # Guardar raw header y payload para el archivo .mis
            raw_header = header
            raw_payload = payload

            meta = decode_domsat_header(header)
            if meta["dcp"].upper() != dcp_id.upper(): return False
            
            # ==================== GUARDAR HEADER GOES ====================
            guardar_header_goes(dcp_id, meta)

            #ts_str = meta["timestamp"].strftime("%Y%m%d_%H%M%S")                        

            valores = []
            if payload:
                try:
                    payload_clean = payload.decode('ascii')[4:].encode('ascii')
                    valores = ConvertFrom6BitsSutron(payload_clean)
                    id_asignado = ESTACIONES[dcp_id].get("id_asignado", "")
                    
                    # Insertar en BD
                    insertar_datos_mapeados(dcp_id, id_asignado, meta["timestamp"], valores)

                    # ==================== GENERAR ARCHIVO .MIS ====================
                    # Obtener sensores configurados para esta estación
                    sensores_estacion = SENSORES.get(id_asignado, [])
                    
                    # [DEBUG] Diagnóstico de falta de datos en .mis
                    if not sensores_estacion:
                        print(f"[WARN MIS] No se encontraron sensores para IdAsignado='{id_asignado}' (DCP: {dcp_id})")
                        if id_asignado == "":
                            print(f"[WARN MIS] El IdAsignado está vacío. Verifique tabla NV_GoesSGD campo IdAsignado.")
                        elif id_asignado not in SENSORES:
                            print(f"[WARN MIS] El IdAsignado '{id_asignado}' no existe en keys de SENSORES (Total keys: {len(SENSORES)})")
                    else:
                        print(f"[DEBUG MIS] Generando MIS para {dcp_id} (IdAsignado: {id_asignado}) con {len(sensores_estacion)} sensores.")

                    generar_archivo_mis(dcp_id, meta, raw_header, raw_payload, valores, ESTACIONES[dcp_id], sensores_estacion)

                except Exception as e:
                    print(f"[WARN] Error procesando mensaje: {e}")

            print(f"[OK] Guardado")
            log_to_pg(dcp_id, True, meta["timestamp"], host)
            return True

    except Exception as e:
        print(f"[ERROR] {host}: {e}")
        return False

def fetch_messages_for_dcp(dcp_id: str):
    data = ESTACIONES.get(dcp_id)
    if not data:
        print(f"[WARN] {dcp_id} no en BD")
        return
    nombre = data["nombre"]
    print(f"[START] {dcp_id} | {nombre}")
    for host in HOSTS:
        if fetch_from_host(host, dcp_id, nombre):
            break
    else:
        print(f"[FAIL] Sin datos para {dcp_id}")
        log_to_pg(dcp_id, False, None, None)

def fetch_messages_for_dcp_wrapper(dcp_id):
    if dcp_id not in ESTACIONES:
        print(f"[WARN] {dcp_id} ya no existe en configuración, ignorando.")
        return schedule.CancelJob
    data = ESTACIONES[dcp_id]
    nombre = data["nombre"]
    print(f"[INFO] {dcp_id} | {nombre} en minuto {datetime.datetime.now().minute:02d}")
    fetch_messages_for_dcp(dcp_id)

# ==================== RECARGA DE CONFIGURACIÓN ====================
_scheduled_dcp_ids = set()

def _programar_estacion(dcp_id, data):
    """Programa la descarga de una estación en el scheduler."""
    minuto = data["minuto"]
    nombre = data["nombre"]
    minuto_ejec = (minuto + 1) % 60
    carry = (minuto + 2) // 60

    if carry:
        schedule.every().hour.at(":00").do(lambda did=dcp_id: fetch_messages_for_dcp_wrapper(did)).tag(f"dcp_{dcp_id}")
        print(f"[SCHED+] {dcp_id} | {nombre} → :00 (próxima hora)")
    else:
        schedule.every().hour.at(f":{minuto_ejec:02d}").do(lambda did=dcp_id: fetch_messages_for_dcp_wrapper(did)).tag(f"dcp_{dcp_id}")
        print(f"[SCHED+] {dcp_id} | {nombre} → :{minuto_ejec:02d}")

def recargar_configuracion():
    """Recarga estaciones y sensores desde SQL Server.
    Si hay cambios (nuevas estaciones, eliminadas o modificadas), actualiza el scheduler."""
    global ESTACIONES, SENSORES, UMBRALES_PRECIP, CUENCAS_ESTACION, _scheduled_dcp_ids
    try:
        print("[RELOAD] Verificando cambios en SQL Server...")
        nuevas_estaciones = load_estaciones_from_sql()
        nuevos_sensores = load_sensores_from_sql()

        if not nuevas_estaciones:
            print("[RELOAD] No se pudieron cargar estaciones, manteniendo configuración actual.")
            return

        # Asignar posiciones a sensores nuevos
        for id_asignado, sensores in nuevos_sensores.items():
            rango_tx = 60
            for dcp_id, datos_estacion in nuevas_estaciones.items():
                if datos_estacion.get("id_asignado") == id_asignado:
                    rango_tx = datos_estacion.get("rango_transmision", 60)
                    break
            nuevos_sensores[id_asignado] = asignar_posiciones_sensores(sensores, rango_tx)

        # Detectar cambios
        ids_actuales = set(ESTACIONES.keys())
        ids_nuevos = set(nuevas_estaciones.keys())

        agregadas = ids_nuevos - ids_actuales
        eliminadas = ids_actuales - ids_nuevos
        # Detectar estaciones con minuto modificado
        modificadas = set()
        for dcp_id in ids_actuales & ids_nuevos:
            if ESTACIONES[dcp_id].get("minuto") != nuevas_estaciones[dcp_id].get("minuto"):
                modificadas.add(dcp_id)

        if not agregadas and not eliminadas and not modificadas:
            # Aún así actualizamos sensores (podrían cambiar cotas, límites, etc.)
            SENSORES = nuevos_sensores
            ESTACIONES = nuevas_estaciones
            UMBRALES_PRECIP = load_umbrales_precipitacion()
            CUENCAS_ESTACION = load_cuencas_from_sql()
            print("[RELOAD] Sin cambios en estaciones. Sensores, umbrales y cuencas actualizados.")
            return

        # Aplicar cambios
        if eliminadas:
            for dcp_id in eliminadas:
                schedule.clear(f"dcp_{dcp_id}")
                _scheduled_dcp_ids.discard(dcp_id)
            print(f"[RELOAD] Estaciones eliminadas del scheduler: {eliminadas}")

        if modificadas:
            for dcp_id in modificadas:
                schedule.clear(f"dcp_{dcp_id}")
                _programar_estacion(dcp_id, nuevas_estaciones[dcp_id])
            print(f"[RELOAD] Estaciones reprogramadas: {modificadas}")

        if agregadas:
            for dcp_id in agregadas:
                _programar_estacion(dcp_id, nuevas_estaciones[dcp_id])
                _scheduled_dcp_ids.add(dcp_id)
            print(f"[RELOAD] Nuevas estaciones agregadas al scheduler: {agregadas}")

        ESTACIONES = nuevas_estaciones
        SENSORES = nuevos_sensores
        UMBRALES_PRECIP = load_umbrales_precipitacion()
        CUENCAS_ESTACION = load_cuencas_from_sql()
        print(f"[RELOAD] Configuración actualizada. Total estaciones: {len(ESTACIONES)}")

    except Exception as e:
        print(f"[ERROR RELOAD] {e}")

# ==================== SCHEDULER ====================
def main():
    global _scheduled_dcp_ids
    reload_minutes = config.getint('settings', 'reload_interval_minutes', fallback=5)

    print("[INFO] Programando descargas (+1 minuto)...")
    for dcp_id, data in ESTACIONES.items():
        _programar_estacion(dcp_id, data)
        _scheduled_dcp_ids.add(dcp_id)

    # Programar recarga periódica de configuración
    schedule.every(reload_minutes).minutes.do(recargar_configuracion)
    print(f"[INFO] Recarga automática de configuración cada {reload_minutes} minutos.")

    print("[INFO] Scheduler activo...")
    while True:
        schedule.run_pending()
        time.sleep(30)

# ==================== CARGA INICIAL ====================
_ensure_pg_tables()
ESTACIONES = load_estaciones_from_sql()
SENSORES = load_sensores_from_sql()
UMBRALES_PRECIP = load_umbrales_precipitacion()
CUENCAS_ESTACION = load_cuencas_from_sql()

# Asignar posiciones considerando el rango de transmisión de cada estación
for id_asignado, sensores in SENSORES.items():
    # Buscar el rango de transmisión de esta estación
    rango_tx = 60  # Default
    for dcp_id, datos_estacion in ESTACIONES.items():
        if datos_estacion.get("id_asignado") == id_asignado:
            rango_tx = datos_estacion.get("rango_transmision", 60)
            break
    
    SENSORES[id_asignado] = asignar_posiciones_sensores(sensores, rango_tx)

# ==================== ENTRADA ====================
if __name__ == "__main__":
    import sys
    if "--manual" in sys.argv:
        print("[MANUAL] Ejecutando todas las estaciones...")
        for dcp_id in ESTACIONES:
            fetch_messages_for_dcp(dcp_id)
        print("[MANUAL] Finalizado.")
    else:
        main()

    # for dcp_id in ESTACIONES:
    #         fetch_messages_for_dcp(dcp_id)
    # print("[MANUAL] Finalizado.")