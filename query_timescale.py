import subprocess
import sys

# Try to install psycopg2-binary if not available
try:
    import psycopg2
except ImportError:
    subprocess.check_call([sys.executable, '-m', 'pip', 'install', 'psycopg2-binary', '-q'])
    import psycopg2

conn = psycopg2.connect(
    host='atlas16.ddns.net',
    database='mycloud_timescale',
    user='postgres',
    password='Cfe123pass',
    connect_timeout=10
)
cur = conn.cursor()

# 1. Check resumen_horario - latest data
print("=== resumen_horario ===")
cur.execute("SELECT COUNT(*), MIN(ts), MAX(ts) FROM public.resumen_horario")
row = cur.fetchone()
print(f"Count: {row[0]}, Min ts: {row[1]}, Max ts: {row[2]}")

# 2. Check what variables exist
cur.execute("SELECT variable, COUNT(*) FROM public.resumen_horario GROUP BY variable ORDER BY COUNT(*) DESC LIMIT 10")
print("\nVariables in resumen_horario:")
for r in cur.fetchall():
    print(f"  {r[0]}: {r[1]} rows")

# 3. Recent data in last 48 hours
cur.execute("SELECT COUNT(*) FROM public.resumen_horario WHERE ts >= NOW() - INTERVAL '48 hours'")
print(f"\nRows in last 48h: {cur.fetchone()[0]}")

# 4. Check ultimas_mediciones
print("\n=== ultimas_mediciones ===")
cur.execute("SELECT COUNT(*), MIN(ts), MAX(ts) FROM public.ultimas_mediciones")
row = cur.fetchone()
print(f"Count: {row[0]}, Min ts: {row[1]}, Max ts: {row[2]}")

# 5. Check estatus_estaciones
print("\n=== estatus_estaciones ===")
cur.execute("SELECT COUNT(*) FROM public.estatus_estaciones")
print(f"Count: {cur.fetchone()[0]}")

# 6. Check dcp_datos table
print("\n=== dcp_datos ===")
try:
    cur.execute("SELECT COUNT(*), MIN(ts), MAX(ts) FROM public.dcp_datos")
    row = cur.fetchone()
    print(f"Count: {row[0]}, Min ts: {row[1]}, Max ts: {row[2]}")
    cur.execute("SELECT COUNT(*) FROM public.dcp_datos WHERE ts >= NOW() - INTERVAL '48 hours'")
    print(f"Rows in last 48h: {cur.fetchone()[0]}")
except Exception as e:
    print(f"Error: {e}")
    conn.rollback()

# 7. Check timezone - what is NOW() on the server?
cur.execute("SELECT NOW(), NOW() AT TIME ZONE 'America/Mexico_City', CURRENT_SETTING('timezone')")
row = cur.fetchone()
print(f"\n=== Server Time ===")
print(f"NOW(): {row[0]}")
print(f"NOW() CDMX: {row[1]}")
print(f"Timezone setting: {row[2]}")

# 8. Sample recent resumen_horario data
print("\n=== Recent resumen_horario (last 5 rows) ===")
cur.execute("SELECT dcp_id, variable, ts, suma, id_asignado FROM public.resumen_horario ORDER BY ts DESC LIMIT 5")
for r in cur.fetchall():
    print(f"  dcp_id={r[0]}, var={r[1]}, ts={r[2]}, suma={r[3]}, id_asignado={r[4]}")

conn.close()
