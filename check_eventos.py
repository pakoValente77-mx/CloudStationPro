import psycopg2

conn = psycopg2.connect(host='atlas16.ddns.net', port=5432,
                        dbname='mycloud_timescale', user='postgres',
                        password='***REDACTED-PG-PASSWORD***')
cur = conn.cursor()

cur.execute('SELECT count(*) FROM eventos_lluvia')
total = cur.fetchone()[0]
cur.execute("SELECT count(*) FROM eventos_lluvia WHERE sospechoso = true")
sosp = cur.fetchone()[0]
cur.execute("SELECT count(*) FROM eventos_lluvia WHERE estado='activo'")
activos = cur.fetchone()[0]
cur.execute("SELECT count(*) FROM eventos_lluvia WHERE estado='finalizado'")
finalizados = cur.fetchone()[0]
print(f'Total: {total} | Activos: {activos} | Finalizados: {finalizados} | Sospechosos: {sosp}')
print()

cur.execute("""SELECT estacion_nombre, estado, acumulado_mm, intensidad_max_mmh,
       duracion_minutos, sospechoso, motivo_sospecha
FROM eventos_lluvia ORDER BY sospechoso DESC, acumulado_mm DESC""")
for r in cur.fetchall():
    tag = ' *** SOSPECHOSO ***' if r[5] else ''
    motivo = f'\n    Motivo: {r[6]}' if r[6] else ''
    print(f'{r[0]:30s} | {r[1]:11s} | {r[2]:7.1f}mm | Max:{r[3]:6.1f}mm/h | {r[4]:4d}min{tag}{motivo}')

conn.close()
