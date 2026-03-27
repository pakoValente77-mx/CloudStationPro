"""
Sincronización de pronóstico de lluvia desde FTP → TimescaleDB.
Descarga CSV de pronóstico numérico, filtra por polígonos de subcuencas,
e inserta en rain_forecast.rain_record (hypertable).

Uso:
  python sync_rain_forecast.py                  # Pronóstico de hoy
  python sync_rain_forecast.py 2026-03-27       # Fecha específica
  python sync_rain_forecast.py --daemon          # Loop continuo cada hora

Formato CSV del FTP:
  LAT,LON,YYYYMMDD_HH,YYYYMMDD_HH,...
  18.5,-93.5,0.2,0.5,1.2,...
"""
import os
import sys
import csv
import io
import ftplib
import time
import configparser
import threading
from datetime import datetime, date, timedelta, timezone
from collections import defaultdict

import psycopg2
from psycopg2.extras import execute_values
from shapely.geometry import Point, Polygon
from shapely.prepared import prep

# ==================== CONFIG ====================
script_dir = os.path.dirname(os.path.abspath(__file__))
config = configparser.ConfigParser()
config.read(os.path.join(script_dir, 'config.ini'))

DB_HOST = config.get('timescaledb', 'host')
DB_PORT = config.get('timescaledb', 'port')
DB_USER = config.get('timescaledb', 'user')
DB_PASS = config.get('timescaledb', 'password')
DB_NAME = config.get('timescaledb', 'database')

# FTP configuration (from Java service's application.yml)
FTP_HOST = '200.4.8.36'
FTP_PORT = 21
FTP_USER = 'usergrijalva'
FTP_PASS = 'Gr1jAlVa.5mN2021'
FTP_PATH = '/modelos_numericos/csv/00z'

# Settings
FORECAST_DAYS = 14
BATCH_SIZE = 5000

# ==================== LOGGING ====================
_original_print = print
def print(*args, **kwargs):
    ts = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    _original_print(f"[{ts}]", *args, **kwargs)


# ==================== DATABASE ====================
def get_conn():
    return psycopg2.connect(
        dbname=DB_NAME, user=DB_USER, password=DB_PASS,
        host=DB_HOST, port=DB_PORT
    )


# ==================== POLYGON LOADING ====================
def load_subcuenca_polygons():
    """Load subcuenca polygons from rain_forecast.subcuenca_poligono table.
    Returns dict: { (cuenca_code, subcuenca_name): PreparedGeometry }
    """
    conn = get_conn()
    cur = conn.cursor()
    cur.execute("""
        SELECT cuenca_code, subcuenca_name, vertex_order, latitude, longitude
        FROM rain_forecast.subcuenca_poligono
        ORDER BY cuenca_code, subcuenca_name, vertex_order
    """)
    rows = cur.fetchall()
    cur.close()
    conn.close()

    # Group by (cuenca_code, subcuenca_name) → list of (lat, lon)
    polygons_raw = defaultdict(list)
    for code, name, order, lat, lon in rows:
        polygons_raw[(code, name)].append((lon, lat))  # Shapely: (x=lon, y=lat)

    polygons = {}
    for key, coords in polygons_raw.items():
        if len(coords) >= 3:
            poly = Polygon(coords)
            if poly.is_valid:
                polygons[key] = prep(poly)
            else:
                # Try to fix invalid polygon
                poly = poly.buffer(0)
                polygons[key] = prep(poly)

    print(f"Cargados {len(polygons)} polígonos de subcuencas")
    return polygons


# ==================== FTP DOWNLOAD ====================
def download_forecast_csv(target_date):
    """Download forecast CSV from FTP for given date.
    Returns (file_content_bytes, file_name, ftp_timestamp) or (None, None, None).
    """
    date_str = target_date.strftime('%Y%m%d')
    file_name = f'pronostico_ens_{date_str}_00.csv'

    print(f"Conectando a FTP {FTP_HOST}...")
    try:
        ftp = ftplib.FTP()
        ftp.connect(FTP_HOST, FTP_PORT, timeout=30)
        ftp.login(FTP_USER, FTP_PASS)
        ftp.set_pasv(True)
        ftp.cwd(FTP_PATH)

        # Check file exists
        files = ftp.nlst()
        if file_name not in files:
            print(f"Archivo {file_name} no encontrado en FTP. Archivos disponibles: {len(files)}")
            ftp.quit()
            return None, None, None

        # Get modification time
        mod_time = None
        try:
            resp = ftp.sendcmd(f'MDTM {file_name}')
            mod_time = datetime.strptime(resp[4:], '%Y%m%d%H%M%S').replace(tzinfo=timezone.utc)
        except Exception:
            mod_time = datetime.now(timezone.utc)

        # Download
        buf = io.BytesIO()
        ftp.retrbinary(f'RETR {file_name}', buf.write)
        ftp.quit()

        content = buf.getvalue()
        print(f"Descargado {file_name}: {len(content):,} bytes")
        return content, file_name, mod_time

    except Exception as e:
        print(f"Error FTP: {e}")
        return None, None, None


# ==================== CSV PARSING ====================
def parse_forecast_csv(content_bytes, polygons):
    """Parse forecast CSV and filter by subcuenca polygons.
    Returns list of tuples: (ts, forecast_date, cuenca_code, subcuenca_name, lat, lon, rain_mm)
    """
    text = content_bytes.decode('utf-8', errors='replace')
    reader = csv.reader(io.StringIO(text))

    # Header row
    header = next(reader)
    if len(header) < 3:
        print(f"CSV inválido: solo {len(header)} columnas")
        return []

    # Parse timestamp columns (skip LAT, LON)
    timestamps = []
    for h in header[2:]:
        h = h.strip()
        try:
            if len(h) >= 11 and '_' in h:
                # Format: YYYYMMDD_HH
                dt_str = h[:8]
                hr_str = h[9:11]
                dt = datetime.strptime(dt_str, '%Y%m%d').replace(
                    hour=int(hr_str), tzinfo=timezone.utc
                )
                timestamps.append(dt)
            else:
                timestamps.append(None)
        except (ValueError, IndexError):
            timestamps.append(None)

    valid_ts = [t for t in timestamps if t is not None]
    if not valid_ts:
        print("No se pudieron parsear timestamps del CSV")
        return []

    forecast_date = valid_ts[0].date()
    print(f"Pronóstico: {forecast_date}, {len(valid_ts)} timestamps, "
          f"rango: {valid_ts[0].strftime('%Y-%m-%d %H:%M')} → {valid_ts[-1].strftime('%Y-%m-%d %H:%M')}")

    # Parse data rows + polygon filtering
    records = []
    rows_total = 0
    rows_inside = 0

    for row in reader:
        if len(row) < 3:
            continue
        try:
            lat = float(row[0].strip())
            lon = float(row[1].strip())
        except (ValueError, IndexError):
            continue

        rows_total += 1
        point = Point(lon, lat)  # Shapely: (x=lon, y=lat)

        # Find which subcuenca this point belongs to
        matched_key = None
        for key, prepared_poly in polygons.items():
            if prepared_poly.contains(point):
                matched_key = key
                break

        if matched_key is None:
            continue  # Point outside all subcuencas

        rows_inside += 1
        cuenca_code, subcuenca_name = matched_key

        for i, ts in enumerate(timestamps):
            if ts is None:
                continue
            col_idx = i + 2
            if col_idx >= len(row):
                continue
            try:
                rain_val = float(row[col_idx].strip())
            except (ValueError, IndexError):
                rain_val = 0.0

            records.append((
                ts,              # ts
                forecast_date,   # forecast_date
                cuenca_code,     # cuenca_code
                subcuenca_name,  # subcuenca_name
                lat,             # latitude
                lon,             # longitude
                rain_val         # rain_mm
            ))

    print(f"Puntos grilla: {rows_total} total, {rows_inside} dentro de subcuencas, "
          f"{len(records)} registros generados")
    return records


# ==================== DATABASE INSERT ====================
def save_forecast(target_date, file_name, mod_time, records):
    """Save forecast metadata and rain records to TimescaleDB."""
    if not records:
        print("Sin registros para guardar.")
        return

    conn = get_conn()
    cur = conn.cursor()

    try:
        # Upsert forecast metadata
        cur.execute("""
            INSERT INTO rain_forecast.forecast (forecast_date, model_run, file_name, downloaded_at, last_update, record_count)
            VALUES (%s, '00z', %s, NOW(), %s, %s)
            ON CONFLICT (forecast_date, model_run) 
            DO UPDATE SET 
                last_update = EXCLUDED.last_update,
                downloaded_at = NOW(),
                record_count = EXCLUDED.record_count,
                file_name = EXCLUDED.file_name
            RETURNING id
        """, (target_date, file_name, mod_time, len(records)))
        forecast_id = cur.fetchone()[0]

        # Delete old records for this forecast date
        cur.execute("""
            DELETE FROM rain_forecast.rain_record 
            WHERE forecast_date = %s
        """, (target_date,))
        deleted = cur.rowcount
        if deleted > 0:
            print(f"Eliminados {deleted:,} registros previos para {target_date}")

        # Batch insert
        total_inserted = 0
        for i in range(0, len(records), BATCH_SIZE):
            batch = records[i:i + BATCH_SIZE]
            execute_values(cur, """
                INSERT INTO rain_forecast.rain_record 
                (ts, forecast_date, cuenca_code, subcuenca_name, latitude, longitude, rain_mm)
                VALUES %s
            """, batch)
            total_inserted += len(batch)

        conn.commit()
        print(f"Guardados {total_inserted:,} registros para pronóstico {target_date} (id={forecast_id})")

    except Exception as e:
        conn.rollback()
        print(f"Error guardando: {e}")
        raise
    finally:
        cur.close()
        conn.close()


# ==================== MAIN SYNC ====================
def sync_forecast(target_date=None):
    """Main sync: download → parse → filter → save."""
    if target_date is None:
        target_date = date.today()

    print(f"\n{'='*50}")
    print(f"  SYNC PRONÓSTICO: {target_date}")
    print(f"{'='*50}")

    # 1. Check if already downloaded with same timestamp
    conn = get_conn()
    cur = conn.cursor()
    cur.execute("""
        SELECT last_update FROM rain_forecast.forecast 
        WHERE forecast_date = %s AND model_run = '00z'
    """, (target_date,))
    existing = cur.fetchone()
    cur.close()
    conn.close()

    # 2. Download CSV from FTP
    content, file_name, mod_time = download_forecast_csv(target_date)
    if content is None:
        print("No se pudo obtener el CSV del FTP.")
        return False

    # 3. Skip if same version
    if existing and existing[0] and mod_time:
        if existing[0] == mod_time:
            print(f"Pronóstico ya descargado (last_update={mod_time}), se omite.")
            return True

    # 4. Load polygons
    polygons = load_subcuenca_polygons()
    if not polygons:
        print("ERROR: No hay polígonos de subcuencas cargados. Ejecuta setup_rain_forecast.py primero.")
        return False

    # 5. Parse + filter
    records = parse_forecast_csv(content, polygons)

    # 6. Save
    save_forecast(target_date, file_name, mod_time, records)
    return True


def daemon_mode():
    """Run sync every hour at minute 40 (matching Java service schedule)."""
    print("Modo daemon: sincronización cada hora en minuto 40")

    while True:
        now = datetime.now()
        # Calculate next :40
        if now.minute < 40:
            next_run = now.replace(minute=40, second=0, microsecond=0)
        else:
            next_run = (now + timedelta(hours=1)).replace(minute=40, second=0, microsecond=0)

        wait_seconds = (next_run - now).total_seconds()
        print(f"Próxima ejecución: {next_run.strftime('%H:%M:%S')} (en {wait_seconds:.0f}s)")
        time.sleep(max(wait_seconds, 1))

        try:
            sync_forecast()
        except Exception as e:
            print(f"Error en sync: {e}")


# ==================== ENTRY POINT ====================
if __name__ == '__main__':
    if len(sys.argv) > 1:
        if sys.argv[1] == '--daemon':
            daemon_mode()
        else:
            # Parse date argument
            try:
                target = datetime.strptime(sys.argv[1], '%Y-%m-%d').date()
            except ValueError:
                print(f"Formato de fecha inválido: {sys.argv[1]}. Use YYYY-MM-DD")
                sys.exit(1)
            sync_forecast(target)
    else:
        sync_forecast()
