import psycopg2

conn = psycopg2.connect(
    host='atlas16.ddns.net', port=5432,
    user='postgres', password='***REDACTED-PG-PASSWORD***',
    database='mycloud_timescale'
)
cur = conn.cursor()

cur.execute("SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'alertas_precipitacion')")
existe = cur.fetchone()[0]
print(f"Tabla existe: {existe}")

if existe:
    cur.execute("SELECT COUNT(*) FROM alertas_precipitacion")
    print(f"Total alertas: {cur.fetchone()[0]}")

    cur.execute("""
        SELECT ts, id_asignado, estacion_nombre, umbral_nombre,
               valor_referencia, valor_medido, operador, periodo_minutos,
               color, activa, notificada
        FROM alertas_precipitacion
        ORDER BY ts DESC
        LIMIT 20
    """)
    cols = [d[0] for d in cur.description]
    rows = cur.fetchall()
    if rows:
        print()
        print(" | ".join(cols))
        print("-" * 120)
        for r in rows:
            print(" | ".join(str(v) for v in r))
    else:
        print("(sin registros)")
else:
    print("La tabla aun no se ha creado (se crea en la primera ejecucion del script)")

conn.close()
