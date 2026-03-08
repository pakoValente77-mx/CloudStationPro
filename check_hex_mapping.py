import psycopg2

HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "***REDACTED-PG-PASSWORD***"
DBNAME = "mycloud_timescale" 

try:
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    cur = conn.cursor()
    
    print("Checking for 15B5333C in resumen_horario...")
    cur.execute("SELECT DISTINCT id_asignado FROM public.resumen_horario WHERE dcp_id = '15B5333C'")
    rows = cur.fetchall()
    if rows:
        for r in rows:
            print(f"  id_asignado: {r[0]}")
    else:
        print("  NOT FOUND in resumen_horario.")

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
