import psycopg2

try:
    conn = psycopg2.connect("host=192.168.1.72 dbname=mycloud_timescale user=postgres password=***REDACTED-PG-PASSWORD***")
    cur = conn.cursor()

    cur.execute("SELECT column_name, data_type FROM information_schema.columns WHERE table_name = 'resumen_horario';")
    print("Columns in resumen_horario:")
    for row in cur.fetchall():
        print(row)

    cur.execute("SELECT column_name, data_type FROM information_schema.columns WHERE table_name = 'dcp_datos';")
    print("\nColumns in dcp_datos:")
    for row in cur.fetchall():
        print(row)

    cur.execute("""
        SELECT ts, maximo, suma 
        FROM resumen_horario 
        WHERE id_asignado = 'CFE0000007' AND variable = 'nivel_de_agua' 
        ORDER BY ts DESC LIMIT 5;
    """)
    print("\nRecent rows for CFE0000007 (nivel_de_agua):")
    for row in cur.fetchall():
        print(row)

    cur.execute("""
        SELECT ts, valor, valido 
        FROM dcp_datos 
        WHERE dcp_id = (SELECT dcp_id FROM resumen_horario WHERE id_asignado = 'CFE0000007' LIMIT 1) 
        AND variable = 'nivel_de_agua' 
        ORDER BY ts DESC LIMIT 5;
    """)
    print("\nRecent raw rows for CFE0000007 (nivel_de_agua):")
    for row in cur.fetchall():
        print(row)

except Exception as e:
    print(e)
