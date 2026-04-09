import psycopg2

conn = psycopg2.connect(
    host='atlas16.ddns.net',
    database='mycloud_timescale',
    user='postgres',
    password='***REDACTED-PG-PASSWORD***',
    connect_timeout=15
)
cur = conn.cursor()

# 1. Check if dcp_datos has data in today's gap hours (05:00-13:00) for precipitation
print("=== dcp_datos: ALL precipitation data today by hour ===")
cur.execute("""
    SELECT date_trunc('hour', ts) as hour, COUNT(*), COUNT(DISTINCT dcp_id)
    FROM public.dcp_datos
    WHERE variable = 'precipitaci\u00f3n'
    AND ts >= CURRENT_DATE
    AND ts < CURRENT_DATE + INTERVAL '1 day'
    GROUP BY date_trunc('hour', ts)
    ORDER BY hour
""")
for r in cur.fetchall():
    print(f"  hour={r[0]}, rows={r[1]}, stations={r[2]}")

# 2. Same for resumen_horario
print("\n=== resumen_horario: ALL precipitation data today by hour ===")
cur.execute("""
    SELECT date_trunc('hour', ts) as hour, COUNT(*), COUNT(DISTINCT dcp_id)
    FROM public.resumen_horario
    WHERE variable = 'precipitaci\u00f3n'
    AND ts >= CURRENT_DATE
    AND ts < CURRENT_DATE + INTERVAL '1 day'
    GROUP BY date_trunc('hour', ts)
    ORDER BY hour
""")
for r in cur.fetchall():
    print(f"  hour={r[0]}, rows={r[1]}, stations={r[2]}")

# 3. Check how resumen_horario gets populated - does the exact query from the C# code work with timestamp parameter types?
print("\n=== Simulating EXACT C# query with proper UTC timestamps ===")
from datetime import datetime, timezone, timedelta

cur.execute("SELECT CURRENT_DATE, NOW()")
server_date, server_now = cur.fetchone()
print(f"Server date: {server_date}, Server now: {server_now}")

now_cdmx = server_now.replace(tzinfo=None)
end_date_cdmx = datetime(now_cdmx.year, now_cdmx.month, now_cdmx.day) + timedelta(days=1)
start_time_cdmx = end_date_cdmx - timedelta(days=1) + timedelta(hours=6)
end_time_cdmx = end_date_cdmx + timedelta(hours=6)

start_time_utc = start_time_cdmx + timedelta(hours=6)
end_time_utc = end_time_cdmx + timedelta(hours=6+1)

print(f"start_time_utc: {start_time_utc}")
print(f"end_time_utc: {end_time_utc}")

cur.execute("""
    SELECT 
        dcp_id,
        date_trunc('hour', ts) as hour,
        SUM(suma) as value,
        true as is_valid
    FROM public.resumen_horario
    WHERE variable = %s
      AND ts >= %s::timestamptz
      AND ts < %s::timestamptz
    GROUP BY dcp_id, date_trunc('hour', ts)
    ORDER BY dcp_id, hour
""", ('precipitaci\u00f3n', 
      start_time_utc.strftime('%Y-%m-%d %H:%M:%S+00'),
      end_time_utc.strftime('%Y-%m-%d %H:%M:%S+00')))

results = cur.fetchall()
print(f"\nTotal rows returned: {len(results)}")
print(f"Distinct stations: {len(set(r[0] for r in results))}")
if results:
    for r in results[:10]:
        print(f"  dcp_id={r[0]}, hour={r[1]}, value={r[2]}")

# 4. Check if ALL data (precipitaci\u00f3n) has the same pattern - is today's data collection broken?
print("\n=== dcp_datos: last 5 precipitation rows ===")
cur.execute("""
    SELECT ts, dcp_id, valor 
    FROM public.dcp_datos 
    WHERE variable = 'precipitaci\u00f3n' 
    ORDER BY ts DESC 
    LIMIT 5
""")
for r in cur.fetchall():
    print(f"  ts={r[0]}, dcp_id={r[1]}, valor={r[2]}")

# 5. Check if the mycloud process is inserting - look at the MOST RECENT insertion times
print("\n=== Most recent dcp_datos inserts (any variable) ===")
cur.execute("""
    SELECT MAX(ts), COUNT(*) 
    FROM public.dcp_datos 
    WHERE ts >= NOW() - INTERVAL '1 hour'
""")
row = cur.fetchone()
print(f"  Last hour: max_ts={row[0]}, count={row[1]}")

conn.close()
