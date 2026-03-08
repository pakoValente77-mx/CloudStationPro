import psycopg2

HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "Cfe123pass"
DBNAME = "mycloud_timescale" 

try:
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    cur = conn.cursor()
    
    print("Checking for FCHAY07055 in resumen_horario...")
    cur.execute("SELECT DISTINCT dcp_id FROM public.resumen_horario WHERE id_asignado = 'FCHAY07055'")
    rows = cur.fetchall()
    if rows:
        for r in rows:
            print(f"  dcp_id: {r[0]}")
    else:
        print("  NOT FOUND in resumen_horario.")

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
