import psycopg2

conn = psycopg2.connect(
    host='atlas16.ddns.net',
    database='mycloud_timescale',
    user='postgres',
    password='Cfe123pass',
    connect_timeout=10
)
cur = conn.cursor()

# 1. Check column type of ts in resumen_horario
print("=== Column types in resumen_horario ===")
cur.execute('''
    SELECT column_name, data_type, is_nullable 
    FROM information_schema.columns 
    WHERE table_name = 'resumen_horario' AND table_schema = 'public'
    ORDER BY ordinal_position
''')
for r in cur.fetchall():
    print(f"  {r[0]}: {r[1]} (nullable={r[2]})")

# 2. Check column type of ts in dcp_datos
print("\n=== Column types in dcp_datos ===")
cur.execute('''
    SELECT column_name, data_type, is_nullable 
    FROM information_schema.columns 
    WHERE table_name = 'dcp_datos' AND table_schema = 'public'
    ORDER BY ordinal_position
''')
for r in cur.fetchall():
    print(f"  {r[0]}: {r[1]} (nullable={r[2]})")

# 3. Check if timestamps match timezone expectations
print("\n=== Timezone test on resumen_horario ===")
cur.execute('''
    SELECT ts, ts AT TIME ZONE 'UTC' as ts_utc, ts AT TIME ZONE 'America/Mexico_City' as ts_cdmx
    FROM public.resumen_horario 
    WHERE variable = 'precipitaci\u00f3n'
    ORDER BY ts DESC LIMIT 3
''')
for r in cur.fetchall():
    print(f"  ts={r[0]}, ts_utc={r[1]}, ts_cdmx={r[2]}")

# 4. Check id_asignado mapping
print("\n=== Stations with id_asignado in resumen_horario ===")
cur.execute('''
    SELECT COUNT(DISTINCT dcp_id), 
           COUNT(DISTINCT id_asignado),
           COUNT(DISTINCT CASE WHEN id_asignado IS NOT NULL THEN dcp_id END)
    FROM public.resumen_horario
''')
row = cur.fetchone()
print(f"  Total distinct dcp_ids: {row[0]}")
print(f"  Total distinct id_asignados: {row[1]}")
print(f"  dcp_ids with id_asignado: {row[2]}")

# 5. Check sample of id_asignado values
print("\n=== Sample id_asignado mappings ===")
cur.execute('''
    SELECT DISTINCT dcp_id, id_asignado 
    FROM public.resumen_horario 
    WHERE id_asignado IS NOT NULL 
    LIMIT 15
''')
for r in cur.fetchall():
    print(f"  dcp_id={r[0]} -> id_asignado={r[1]}")

# 6. Simulate the hourly report query timing
print("\n=== Simulating hourly report query ===")
cur.execute('''
    SELECT COUNT(*), MIN(ts), MAX(ts) FROM public.resumen_horario
    WHERE variable = 'precipitaci\u00f3n'
    AND ts >= (CURRENT_DATE + INTERVAL '6 hours')
    AND ts < (CURRENT_DATE + INTERVAL '1 day' + INTERVAL '6 hours')
''')
row = cur.fetchone()
print(f"  Today's report (local tz): Count={row[0]}, Min={row[1]}, Max={row[2]}")

# 7. Now simulate with UTC conversion like C# does
print("\n=== Simulating with UTC timestamps like C# sends ===")
cur.execute('''
    SELECT COUNT(*), MIN(ts), MAX(ts) FROM public.resumen_horario
    WHERE variable = 'precipitaci\u00f3n'
    AND ts >= (CURRENT_DATE + INTERVAL '12 hours') AT TIME ZONE 'UTC'
    AND ts < (CURRENT_DATE + INTERVAL '1 day' + INTERVAL '13 hours') AT TIME ZONE 'UTC'
''')
row = cur.fetchone()
print(f"  With UTC conversion: Count={row[0]}, Min={row[1]}, Max={row[2]}")

# 8. What data exists for today in precipitation?
print("\n=== Precipitation data for today (all hours) ===")
cur.execute('''
    SELECT date_trunc('hour', ts) as hour, COUNT(*), SUM(suma)
    FROM public.resumen_horario
    WHERE variable = 'precipitaci\u00f3n'
    AND ts >= CURRENT_DATE
    GROUP BY date_trunc('hour', ts)
    ORDER BY hour
''')
for r in cur.fetchall():
    print(f"  hour={r[0]}, count={r[1]}, total_suma={r[2]}")

# 9. Yesterday's data
print("\n=== Precipitation data for yesterday (report window 6am-6am) ===")
cur.execute('''
    SELECT date_trunc('hour', ts) as hour, COUNT(*), SUM(suma)
    FROM public.resumen_horario
    WHERE variable = 'precipitaci\u00f3n'
    AND ts >= (CURRENT_DATE - INTERVAL '1 day' + INTERVAL '6 hours')
    AND ts < (CURRENT_DATE + INTERVAL '6 hours')
    GROUP BY date_trunc('hour', ts)
    ORDER BY hour
''')
rows = cur.fetchall()
print(f"  Total hours with data: {len(rows)}")
for r in rows[:5]:
    print(f"  hour={r[0]}, count={r[1]}, total_suma={r[2]}")
if len(rows) > 5:
    print(f"  ... and {len(rows)-5} more hours")

conn.close()
