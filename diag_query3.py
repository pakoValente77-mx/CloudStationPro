import psycopg2
conn = psycopg2.connect(host='atlas16.ddns.net', database='mycloud_timescale', user='postgres', password='***REDACTED-PG-PASSWORD***', connect_timeout=10)
conn.autocommit = True
cur = conn.cursor()
cur.execute("SET statement_timeout = '15s'")

try:
    print("=== Sample id_asignado mappings ===")
    cur.execute("SELECT DISTINCT dcp_id, id_asignado FROM public.resumen_horario WHERE id_asignado IS NOT NULL AND ts > NOW() - INTERVAL '7 days' LIMIT 15")
    for r in cur.fetchall():
        print(f"  dcp_id={r[0]} -> id_asignado={r[1]}")
except Exception as e:
    print(f"  ERROR: {e}")

try:
    print("\n=== Simulating hourly report query ===")
    cur.execute('''SELECT COUNT(*), MIN(ts), MAX(ts) FROM public.resumen_horario WHERE variable = 'precipitaci\u00f3n' AND ts >= (CURRENT_DATE + INTERVAL '6 hours') AND ts < (CURRENT_DATE + INTERVAL '1 day' + INTERVAL '6 hours')''')
    row = cur.fetchone()
    print(f"  Today's report (local tz): Count={row[0]}, Min={row[1]}, Max={row[2]}")
except Exception as e:
    print(f"  ERROR: {e}")

try:
    print("\n=== Simulating with UTC timestamps like C# sends ===")
    cur.execute('''SELECT COUNT(*), MIN(ts), MAX(ts) FROM public.resumen_horario WHERE variable = 'precipitaci\u00f3n' AND ts >= (CURRENT_DATE + INTERVAL '12 hours') AT TIME ZONE 'UTC' AND ts < (CURRENT_DATE + INTERVAL '1 day' + INTERVAL '13 hours') AT TIME ZONE 'UTC' ''')
    row = cur.fetchone()
    print(f"  With UTC conversion: Count={row[0]}, Min={row[1]}, Max={row[2]}")
except Exception as e:
    print(f"  ERROR: {e}")

try:
    print("\n=== Precipitation data for today (all hours) ===")
    cur.execute('''SELECT date_trunc('hour', ts) as hour, COUNT(*), SUM(suma) FROM public.resumen_horario WHERE variable = 'precipitaci\u00f3n' AND ts >= CURRENT_DATE GROUP BY date_trunc('hour', ts) ORDER BY hour''')
    for r in cur.fetchall():
        print(f"  hour={r[0]}, count={r[1]}, total_suma={r[2]}")
except Exception as e:
    print(f"  ERROR: {e}")

try:
    print("\n=== Precipitation data for yesterday (report window 6am-6am) ===")
    cur.execute('''SELECT date_trunc('hour', ts) as hour, COUNT(*), SUM(suma) FROM public.resumen_horario WHERE variable = 'precipitaci\u00f3n' AND ts >= (CURRENT_DATE - INTERVAL '1 day' + INTERVAL '6 hours') AND ts < (CURRENT_DATE + INTERVAL '6 hours') GROUP BY date_trunc('hour', ts) ORDER BY hour''')
    rows = cur.fetchall()
    print(f"  Total hours with data: {len(rows)}")
    for r in rows[:5]:
        print(f"  hour={r[0]}, count={r[1]}, total_suma={r[2]}")
    if len(rows) > 5:
        print(f"  ... and {len(rows)-5} more hours")
except Exception as e:
    print(f"  ERROR: {e}")

conn.close()
print("\nDone.")
