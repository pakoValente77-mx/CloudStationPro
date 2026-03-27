"""
Setup TimescaleDB schema for Rain Forecast integration.
Creates schema rain_forecast with tables:
  - forecast        (metadata per forecast run)
  - subcuenca_poligono (polygon vertices for sub-basins, loaded from KML)
  - rain_record     (hypertable: hourly forecast data per grid point)
"""
import psycopg2
import configparser
import os
import sys
import xml.etree.ElementTree as ET
import re

# ==================== CONFIG ====================
script_dir = os.path.dirname(os.path.abspath(__file__))
config = configparser.ConfigParser()
config.read(os.path.join(script_dir, 'config.ini'))

DB_HOST = config.get('timescaledb', 'host')
DB_PORT = config.get('timescaledb', 'port')
DB_USER = config.get('timescaledb', 'user')
DB_PASS = config.get('timescaledb', 'password')
DB_NAME = config.get('timescaledb', 'database')


def get_conn():
    return psycopg2.connect(
        dbname=DB_NAME, user=DB_USER, password=DB_PASS,
        host=DB_HOST, port=DB_PORT
    )


def create_schema_and_tables():
    conn = get_conn()
    conn.autocommit = True
    cur = conn.cursor()

    print("[*] Creando esquema rain_forecast...")
    cur.execute("CREATE SCHEMA IF NOT EXISTS rain_forecast;")

    # 1. Tabla forecast — metadata por corrida de pronóstico
    print("[*] Creando tabla rain_forecast.forecast...")
    cur.execute("""
        CREATE TABLE IF NOT EXISTS rain_forecast.forecast (
            id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            forecast_date   DATE NOT NULL,
            model_run       VARCHAR(10) DEFAULT '00z',
            file_name       VARCHAR(200),
            downloaded_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            last_update     TIMESTAMPTZ,
            record_count    INTEGER DEFAULT 0,
            UNIQUE(forecast_date, model_run)
        );
    """)

    # 2. Tabla subcuenca_poligono — vértices de polígonos de subcuencas
    print("[*] Creando tabla rain_forecast.subcuenca_poligono...")
    cur.execute("""
        CREATE TABLE IF NOT EXISTS rain_forecast.subcuenca_poligono (
            id              SERIAL PRIMARY KEY,
            cuenca_code     VARCHAR(10) NOT NULL,
            subcuenca_name  VARCHAR(200) NOT NULL,
            vertex_order    INTEGER NOT NULL,
            latitude        DOUBLE PRECISION NOT NULL,
            longitude       DOUBLE PRECISION NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_subcpoly_cuenca 
            ON rain_forecast.subcuenca_poligono(cuenca_code, subcuenca_name);
    """)

    # 3. Tabla rain_record — datos de pronóstico por punto de grilla
    print("[*] Creando tabla rain_forecast.rain_record...")
    cur.execute("""
        CREATE TABLE IF NOT EXISTS rain_forecast.rain_record (
            ts              TIMESTAMPTZ NOT NULL,
            forecast_date   DATE NOT NULL,
            cuenca_code     VARCHAR(10) NOT NULL,
            subcuenca_name  VARCHAR(200) NOT NULL,
            latitude        DOUBLE PRECISION NOT NULL,
            longitude       DOUBLE PRECISION NOT NULL,
            rain_mm         DOUBLE PRECISION NOT NULL DEFAULT 0
        );
    """)

    # Convertir a hypertable si aún no lo es
    cur.execute("""
        SELECT EXISTS (
            SELECT 1 FROM timescaledb_information.hypertables 
            WHERE hypertable_schema = 'rain_forecast' 
            AND hypertable_name = 'rain_record'
        );
    """)
    is_hyper = cur.fetchone()[0]
    if not is_hyper:
        print("[*] Convirtiendo rain_record a hypertable...")
        cur.execute("""
            SELECT create_hypertable(
                'rain_forecast.rain_record', 'ts',
                chunk_time_interval => INTERVAL '7 days',
                if_not_exists => TRUE
            );
        """)
    else:
        print("[*] rain_record ya es hypertable, se omite.")

    # Índices para consultas frecuentes
    cur.execute("""
        CREATE INDEX IF NOT EXISTS idx_rr_forecast_cuenca 
            ON rain_forecast.rain_record(forecast_date, cuenca_code);
        CREATE INDEX IF NOT EXISTS idx_rr_subcuenca_ts
            ON rain_forecast.rain_record(cuenca_code, subcuenca_name, ts);
    """)

    # 4. Vista materializada: resumen por hora y subcuenca
    print("[*] Creando vista rain_forecast.resumen_horario_pronostico...")
    cur.execute("DROP VIEW IF EXISTS rain_forecast.resumen_horario_pronostico;")
    cur.execute("""
        CREATE OR REPLACE VIEW rain_forecast.resumen_horario_pronostico AS
        SELECT 
            ts,
            forecast_date,
            cuenca_code,
            subcuenca_name,
            AVG(rain_mm) AS lluvia_media_mm,
            MAX(rain_mm) AS lluvia_max_mm,
            COUNT(*) AS num_puntos
        FROM rain_forecast.rain_record
        GROUP BY ts, forecast_date, cuenca_code, subcuenca_name;
    """)

    print("[+] Esquema rain_forecast creado exitosamente.")
    cur.close()
    conn.close()


def load_kml_polygons():
    """Parse KML files and load subcuenca polygon vertices into the database."""
    kml_dir = os.path.join(script_dir, 'CloudStationWeb', 'KML Cuencas y Rios Grijalva')

    kml_files = {
        'ANG': 'Subcuencas_ANG.kml',
        'MMT': 'Subcuencas_MMT.kml',
        'MPS': 'Subcuencas_MPS.kml',
        'PEA': 'Subcuencas_PEA.kml',
    }

    ns = {'kml': 'http://www.opengis.net/kml/2.2'}

    conn = get_conn()
    cur = conn.cursor()

    # Clear existing polygon data
    cur.execute("DELETE FROM rain_forecast.subcuenca_poligono;")
    total = 0

    for code, filename in kml_files.items():
        filepath = os.path.join(kml_dir, filename)
        if not os.path.exists(filepath):
            print(f"  [!] No se encontró {filepath}, se omite.")
            continue

        print(f"  Procesando {filename} (cuenca {code})...")
        tree = ET.parse(filepath)
        root = tree.getroot()

        for placemark in root.iter(f'{{{ns["kml"]}}}Placemark'):
            name_el = placemark.find(f'{{{ns["kml"]}}}name')
            if name_el is None or not name_el.text:
                continue
            subcuenca_name = name_el.text.strip()

            # Find coordinates in Polygon
            coords_el = placemark.find(
                f'.//{{{ns["kml"]}}}Polygon/{{{ns["kml"]}}}outerBoundaryIs/'
                f'{{{ns["kml"]}}}LinearRing/{{{ns["kml"]}}}coordinates'
            )
            if coords_el is None or not coords_el.text:
                continue

            coords_text = coords_el.text.strip()
            points = []
            for pair in coords_text.split():
                parts = pair.split(',')
                if len(parts) >= 2:
                    try:
                        lon = float(parts[0])
                        lat = float(parts[1])
                        points.append((lat, lon))
                    except ValueError:
                        continue

            if not points:
                continue

            # Insert vertices
            rows = []
            for i, (lat, lon) in enumerate(points):
                rows.append((code, subcuenca_name, i, lat, lon))

            from psycopg2.extras import execute_values
            execute_values(cur, """
                INSERT INTO rain_forecast.subcuenca_poligono 
                (cuenca_code, subcuenca_name, vertex_order, latitude, longitude)
                VALUES %s
            """, rows)

            total += len(rows)
            print(f"    {subcuenca_name}: {len(points)} vértices")

    conn.commit()
    cur.close()
    conn.close()
    print(f"[+] {total} vértices de polígono cargados en rain_forecast.subcuenca_poligono")


if __name__ == '__main__':
    print("="*60)
    print("  SETUP RAIN FORECAST - TimescaleDB")
    print("="*60)

    create_schema_and_tables()

    print("\n[*] Cargando polígonos de subcuencas desde KML...")
    load_kml_polygons()

    print("\n" + "="*60)
    print("  SETUP COMPLETO")
    print("="*60)
