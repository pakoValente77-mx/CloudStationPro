import psycopg2

HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "***REDACTED-PG-PASSWORD***"
DBNAME = "mycloud_timescale" 

try:
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    cur = conn.cursor()
    
    print("Counting unique mappings in resumen_horario...")
    cur.execute("SELECT count(DISTINCT (dcp_id, id_asignado)) FROM public.resumen_horario")
    count = cur.fetchone()[0]
    print(f"Total unique mappings: {count}")

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
