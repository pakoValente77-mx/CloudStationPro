#!/usr/bin/env python3
"""Consulta precipitación promedio por cuenca/subcuenca."""
import psycopg2, configparser, os

config = configparser.ConfigParser()
config.read(os.path.join(os.path.dirname(__file__), 'config.ini'))

conn = psycopg2.connect(
    host=config['timescaledb']['host'],
    port=config['timescaledb']['port'],
    dbname=config['timescaledb']['database'],
    user=config['timescaledb']['user'],
    password=config['timescaledb']['password']
)
cur = conn.cursor()

print("=" * 90)
print(" PRECIPITACIÓN POR CUENCA - Última hora")
print("=" * 90)

cur.execute("""
    SELECT ts, tipo, nombre, promedio_mm, max_mm, min_mm,
           estaciones_con_dato, estaciones_total, semaforo,
           ultima_actualizacion
    FROM precipitacion_cuenca
    ORDER BY ts DESC, tipo, promedio_mm DESC
    LIMIT 20
""")
for row in cur.fetchall():
    ts, tipo, nombre, prom, mx, mn, con_dato, total, sem, upd = row
    icon = {"verde": "🟢", "amarillo": "🟡", "naranja": "🟠", "rojo": "🔴"}.get(sem, "⚪")
    print(f"{icon} [{tipo.upper():10s}] {nombre:35s} | Prom: {prom:6.1f}mm | "
          f"Max: {mx:6.1f}mm | {con_dato}/{total} est. | {sem.upper()}")

print(f"\nÚltima actualización: {upd}" if cur.rowcount else "\nSin datos")
cur.close()
conn.close()
