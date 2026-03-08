import psycopg2

HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "***REDACTED-PG-PASSWORD***"
DBNAME = "mycloud_timescale" 

try:
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    cur = conn.cursor()
    
    cur.execute("SELECT DISTINCT variable FROM public.ultimas_mediciones ORDER BY variable")
    vars = cur.fetchall()
    print("Variables in public.ultimas_mediciones:")
    for v in vars:
        print(f"  {v[0]}")
        
except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
