import psycopg2

HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "Cfe123pass"
DBNAME = "mycloud_timescale" 

try:
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    cur = conn.cursor()
    
    print("Columns in public.dcp_headers:")
    cur.execute("SELECT column_name FROM information_schema.columns WHERE table_name = 'dcp_headers' AND table_schema = 'public'")
    cols = cur.fetchall()
    for c in cols:
        print(f"  {c[0]}")

    print("\nSample mapping in public.dcp_headers (if any):")
    # Let's check if it has id_asignado (we'll see from columns check above)
    # If not, we'll stick to resumen_horario or look elsewhere.

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
