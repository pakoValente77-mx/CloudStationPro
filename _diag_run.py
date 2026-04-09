import psycopg2

conn = psycopg2.connect(
    host='atlas16.ddns.net',
    database='mycloud_timescale',
    user='postgres',
    password='***REDACTED-PG-PASSWORD***',
    connect_timeout=10
)
cur = conn.cursor()

print('=== dcp_datos: precipitation data today 06:00+ ===')
cur.execute('''
    SELECT date_trunc('hour', ts) as hour, COUNT(*), COUNT(DISTINCT dcp_id)
    FROM public.dcp_datos
    WHERE variable = 'precipitaci\u00f3n'
    AND ts >= CURRENT_DATE + INTERVAL '6 hours'
    GROUP BY date_trunc('hour', ts)
    ORDER BY hour
''')
for r in cur.fetchall():
    print(f'  hour={r[0]}, rows={r[1]}, stations={r[2]}')

print('\n=== resumen_horario: precipitation data today 06:00+ ===')
cur.execute('''
    SELECT date_trunc('hour', ts) as hour, COUNT(*), COUNT(DISTINCT dcp_id)
    FROM public.resumen_horario
    WHERE variable = 'precipitaci\u00f3n'
    AND ts >= CURRENT_DATE + INTERVAL '6 hours'
    GROUP BY date_trunc('hour', ts)
    ORDER BY hour
''')
for r in cur.fetchall():
    print(f'  hour={r[0]}, rows={r[1]}, stations={r[2]}')

print('\n=== dcp_datos: nivel_de_agua data today 06:00+ ===')
cur.execute('''
    SELECT date_trunc('hour', ts) as hour, COUNT(*), COUNT(DISTINCT dcp_id)
    FROM public.dcp_datos
    WHERE variable = 'nivel_de_agua'
    AND ts >= CURRENT_DATE + INTERVAL '6 hours'
    AND EXTRACT(MINUTE FROM ts) = 0
    GROUP BY date_trunc('hour', ts)
    ORDER BY hour
''')
for r in cur.fetchall():
    print(f'  hour={r[0]}, rows={r[1]}, stations={r[2]}')

print('\n=== Latest resumen_horario entry per variable ===')
cur.execute('''
    SELECT variable, MAX(ts) as latest_ts
    FROM public.resumen_horario
    GROUP BY variable
    ORDER BY MAX(ts) DESC
''')
for r in cur.fetchall():
    print(f'  {r[0]}: {r[1]}')

print('\n=== resumen_horario inserts by hour (last 24h) ===')
cur.execute('''
    SELECT date_trunc('hour', ts) as hour, COUNT(*), COUNT(DISTINCT dcp_id)
    FROM public.resumen_horario
    WHERE ts >= NOW() - INTERVAL '24 hours'
    AND variable = 'precipitaci\u00f3n'
    GROUP BY date_trunc('hour', ts)
    ORDER BY hour
''')
for r in cur.fetchall():
    print(f'  hour={r[0]}, rows={r[1]}, stations={r[2]}')

print('\n=== Stations with data in resumen_horario at 14:00+ today ===')
cur.execute('''
    SELECT DISTINCT dcp_id, id_asignado
    FROM public.resumen_horario
    WHERE variable = 'precipitaci\u00f3n'
    AND ts >= CURRENT_DATE + INTERVAL '14 hours'
''')
for r in cur.fetchall():
    print(f'  dcp_id={r[0]}, id_asignado={r[1]}')

conn.close()
