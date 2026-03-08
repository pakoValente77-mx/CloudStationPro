import psycopg2

HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "***REDACTED-PG-PASSWORD***"
DBNAME = "mycloud_timescale" 

try:
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    cur = conn.cursor()
    
    print("Checking for FCHAY07055 in ultimas_mediciones...")
    cur.execute("SELECT dcp_id, variable, valor FROM public.ultimas_mediciones WHERE dcp_id = 'FCHAY07055'")
    rows = cur.fetchall()
    if rows:
        for r in rows:
            print(f"  Var: {r[1]}, Value: {r[2]}")
    else:
        print("  NOT FOUND in ultimas_mediciones.")

    print("\nChecking for FCHAY07055 in estatus_estaciones...")
    cur.execute("SELECT dcp_id, color_estatus FROM public.estatus_estaciones WHERE dcp_id = 'FCHAY07055'")
    rows = cur.fetchall()
    if rows:
        for r in rows:
            print(f"  Color: {r[1]}")
    else:
        print("  NOT FOUND in estatus_estaciones.")

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
