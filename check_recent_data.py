import psycopg2
from datetime import datetime, timedelta

HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "Cfe123pass"
DBNAME = "mycloud_timescale" 

try:
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    cur = conn.cursor()
    
    print("Checking public.estatus_estaciones...")
    cur.execute("SELECT dcp_id, color_estatus, fecha_ultima_tx FROM public.estatus_estaciones LIMIT 10")
    rows = cur.fetchall()
    for r in rows:
        print(f"  ID: {r[0]}, Color: {r[1]}, Last Tx: {r[2]}")
        
    print("\nChecking public.ultimas_mediciones for 'precipitación'...")
    cur.execute("SELECT dcp_id, variable, valor, ts FROM public.ultimas_mediciones WHERE variable = 'precipitación' LIMIT 10")
    rows = cur.fetchall()
    for r in rows:
        print(f"  ID: {r[0]}, Var: {r[1]}, Value: {r[2]}, TS: {r[3]}")

    print("\nChecking latest overall data in public.dcp_datos...")
    cur.execute("SELECT dcp_id, variable, valor, ts FROM public.dcp_datos ORDER BY ts DESC LIMIT 5")
    rows = cur.fetchall()
    for r in rows:
        print(f"  ID: {r[0]}, Var: {r[1]}, Value: {r[2]}, TS: {r[3]}")

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
