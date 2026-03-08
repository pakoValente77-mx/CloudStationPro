import psycopg2

HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "Cfe123pass"
DBNAME = "mycloud_timescale" 

try:
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    cur = conn.cursor()
    
    print("Listing dcp_id from public.estatus_estaciones...")
    cur.execute("SELECT dcp_id FROM public.estatus_estaciones")
    rows = cur.fetchall()
    ids = [r[0] for r in rows]
    print(f"Total IDs: {len(ids)}")
    print(f"Sample IDs: {ids[:20]}")
    
    # Check if FCHAY07055 exists
    if 'FCHAY07055' in ids:
        print("\nFCHAY07055 FOUND in estatus_estaciones!")
    else:
        print("\nFCHAY07055 NOT FOUND in estatus_estaciones.")

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
