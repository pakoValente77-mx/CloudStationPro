import psycopg2

HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "Cfe123pass"
DBNAME = "mycloud_timescale" 

try:
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    cur = conn.cursor()
    
    print("Checking public.resumen_horario for mapping...")
    cur.execute("SELECT DISTINCT dcp_id, id_asignado FROM public.resumen_horario LIMIT 20")
    rows = cur.fetchall()
    for r in rows:
        print(f"  dcp_id: {r[0]}, id_asignado: {r[1]}")

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
