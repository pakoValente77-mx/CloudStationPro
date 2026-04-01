#!/usr/bin/env python3
"""Diagnóstico rápido de huecos en datos GOES."""
import psycopg2
import datetime
import os, sys, configparser

_base_dir = os.path.dirname(os.path.abspath(__file__))
config = configparser.ConfigParser()
config.read(os.path.join(_base_dir, 'config.ini'))

PG_HOST = config.get('timescaledb', 'host', fallback='localhost')
PG_PORT = config.getint('timescaledb', 'port', fallback=5432)
PG_USER = config.get('timescaledb', 'user', fallback='postgres')
PG_PASSWORD = config.get('timescaledb', 'password', fallback='Cfe2026##')
PG_DATABASE = config.get('timescaledb', 'database', fallback='mycloud_timescale')

conn = psycopg2.connect(host=PG_HOST, port=PG_PORT, user=PG_USER, password=PG_PASSWORD, database=PG_DATABASE)
cur = conn.cursor()

print("=" * 70)
print("  DIAGNÓSTICO DE HUECOS - Últimas 24 horas")
print("=" * 70)

# 1) Registros por hora
print(f"\n{'Hora UTC':<22} {'Estaciones':<12} {'Registros'}")
print("-" * 50)
cur.execute("""
    SELECT date_trunc('hour', ts) as hora,
           COUNT(DISTINCT dcp_id) as estaciones,
           COUNT(*) as registros
    FROM dcp_datos
    WHERE ts > NOW() - INTERVAL '24 hours'
    GROUP BY 1
    ORDER BY 1
""")
rows = cur.fetchall()
for row in rows:
    marker = " ⚠️" if row[1] < 20 else ""
    print(f"  {str(row[0]):<22} {row[1]:<12} {row[2]}{marker}")

# 2) Bitácora GOES: último fetch exitoso por estación
print(f"\n{'='*70}")
print("  ÚLTIMA RECEPCIÓN POR ESTACIÓN (top 20 más antiguas)")
print("=" * 70)
cur.execute("""
    SELECT dcp_id, 
           MAX(timestamp_msg) as ultimo_goes,
           EXTRACT(EPOCH FROM (NOW() - MAX(timestamp_msg)))/3600 as horas_sin_datos
    FROM bitacora_goes
    WHERE exito = true
    GROUP BY dcp_id
    ORDER BY 2 ASC
    LIMIT 20
""")
print(f"  {'DCP ID':<14} {'Último GOES (UTC)':<24} {'Horas sin datos'}")
print(f"  {'-'*14} {'-'*24} {'-'*15}")
for row in cur.fetchall():
    horas = row[2] if row[2] else 0
    marker = " ⚠️" if horas > 3 else ""
    print(f"  {row[0]:<14} {str(row[1]):<24} {horas:.1f}h{marker}")

# 3) Estaciones que NO tienen datos en últimas 4 horas
print(f"\n{'='*70}")
print("  ESTACIONES SIN DATOS EN LAS ÚLTIMAS 4 HORAS")
print("=" * 70)
cur.execute("""
    SELECT DISTINCT dcp_id FROM bitacora_goes WHERE exito = true
    EXCEPT
    SELECT DISTINCT dcp_id FROM dcp_datos WHERE ts > NOW() - INTERVAL '4 hours'
""")
sin_datos = cur.fetchall()
if sin_datos:
    print(f"  {len(sin_datos)} estaciones sin datos recientes:")
    for row in sin_datos:
        print(f"    {row[0]}")
else:
    print("  Todas las estaciones tienen datos en las últimas 4 horas ✅")

# 4) ¿El scheduler está corriendo?
print(f"\n{'='*70}")
print("  ÚLTIMO MENSAJE EN BITÁCORA")
print("=" * 70)
cur.execute("""
    SELECT MAX(timestamp_utc), MAX(timestamp_msg), COUNT(*) 
    FROM bitacora_goes 
    WHERE timestamp_utc > NOW() - INTERVAL '1 hour'
""")
row = cur.fetchone()
print(f"  Último registro bitácora : {row[0]}")
print(f"  Último timestamp GOES    : {row[1]}")
print(f"  Registros última hora    : {row[2]}")
if row[2] == 0:
    print("  ⚠️  ¡El scheduler NO está insertando datos! ¿Está corriendo mycloud_all_timescale.py?")

# 5) Hora actual del servidor PG
cur.execute("SELECT NOW(), NOW() AT TIME ZONE 'UTC'")
row = cur.fetchone()
print(f"\n  Hora servidor PG (local) : {row[0]}")
print(f"  Hora servidor PG (UTC)   : {row[1]}")

conn.close()
print()
