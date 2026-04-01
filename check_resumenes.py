#!/usr/bin/env python3
"""Diagnóstico de resúmenes horarios/diarios."""
import psycopg2, os, configparser

_base_dir = os.path.dirname(os.path.abspath(__file__))
config = configparser.ConfigParser()
config.read(os.path.join(_base_dir, 'config.ini'))

conn = psycopg2.connect(
    host=config.get('timescaledb', 'host'),
    port=config.getint('timescaledb', 'port', fallback=5432),
    user=config.get('timescaledb', 'user'),
    password=config.get('timescaledb', 'password'),
    database=config.get('timescaledb', 'database'),
)
cur = conn.cursor()

# 1) Resumen horario
cur.execute("""
    SELECT date_trunc('hour', ts) as hora, COUNT(*) as registros
    FROM resumen_horario 
    WHERE ts > NOW() - INTERVAL '24 hours'
    GROUP BY 1 ORDER BY 1
""")
print("RESUMEN HORARIO (ultimas 24h):")
print(f"  {'Hora':<28} Registros")
print("  " + "-" * 40)
for r in cur.fetchall():
    print(f"  {str(r[0]):<28} {r[1]}")

# 2) Resumen diario
print()
cur.execute("""
    SELECT fecha, COUNT(*) as registros
    FROM resumen_diario
    WHERE fecha >= CURRENT_DATE - 3
    GROUP BY 1 ORDER BY 1
""")
print("RESUMEN DIARIO (ultimos 3 dias):")
print(f"  {'Fecha':<14} Registros")
print("  " + "-" * 30)
for r in cur.fetchall():
    print(f"  {str(r[0]):<14} {r[1]}")

# 3) Comparar datos crudos vs resumen
print()
cur.execute("""
    SELECT COUNT(DISTINCT d.dcp_id) as en_datos,
           (SELECT COUNT(DISTINCT dcp_id) FROM resumen_horario WHERE ts > NOW() - INTERVAL '2 hours') as en_resumen
    FROM dcp_datos d WHERE d.ts > NOW() - INTERVAL '2 hours'
""")
r = cur.fetchone()
print(f"Estaciones con datos crudos (2h): {r[0]}")
print(f"Estaciones con resumen horario (2h): {r[1]}")
if r[0] > r[1]:
    print(f"  ALERTA: {r[0]-r[1]} estaciones SIN resumen horario")

# 4) Estaciones con datos pero sin resumen en ultima hora
print()
cur.execute("""
    SELECT d.dcp_id, COUNT(*) as registros_crudos
    FROM dcp_datos d
    WHERE d.ts > NOW() - INTERVAL '2 hours'
      AND d.dcp_id NOT IN (
          SELECT DISTINCT dcp_id FROM resumen_horario WHERE ts > NOW() - INTERVAL '2 hours'
      )
    GROUP BY d.dcp_id
    ORDER BY 2 DESC
    LIMIT 15
""")
rows = cur.fetchall()
if rows:
    print(f"Estaciones con datos crudos PERO SIN resumen horario:")
    for r in rows:
        print(f"  {r[0]}: {r[1]} registros sin resumir")

# 5) Cuantas horas del dia tienen 0 resumenes
cur.execute("""
    WITH horas AS (
        SELECT generate_series(
            date_trunc('hour', NOW() - INTERVAL '24 hours'),
            date_trunc('hour', NOW()),
            '1 hour'
        ) as hora
    )
    SELECT h.hora, COALESCE(r.cnt, 0) as resumenes
    FROM horas h
    LEFT JOIN (
        SELECT date_trunc('hour', ts) as hora, COUNT(*) as cnt
        FROM resumen_horario
        WHERE ts > NOW() - INTERVAL '24 hours'
        GROUP BY 1
    ) r ON h.hora = r.hora
    WHERE COALESCE(r.cnt, 0) = 0
    ORDER BY 1
""")
vacias = cur.fetchall()
if vacias:
    print(f"\nHoras SIN ningun resumen horario:")
    for r in vacias:
        print(f"  {r[0]}")
else:
    print("\nTodas las horas tienen resumenes OK")

# 6) Donde esta corriendo el scheduler?
print()
cur.execute("""
    SELECT servidor, COUNT(*), MAX(timestamp_utc)
    FROM bitacora_goes
    WHERE timestamp_utc > NOW() - INTERVAL '2 hours'
    GROUP BY servidor
    ORDER BY 2 DESC
""")
print("Servidores LRGS activos (2h):")
for r in cur.fetchall():
    print(f"  {r[0]}: {r[1]} fetches, ultimo: {r[2]}")

conn.close()
