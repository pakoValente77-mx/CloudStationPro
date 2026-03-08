import psycopg2
import time

# Connection details
HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "Cfe123pass"
DBNAME = "mycloud_timescale" 

try:
    print(f"Connecting to {DBNAME} at {HOST}...")
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    cur = conn.cursor()
    
    # 1. Verify Job exists
    cur.execute("""
        SELECT job_id, proc_name, schedule_interval, next_start 
        FROM timescaledb_information.jobs 
        WHERE proc_name = 'actualizar_estatus_estaciones'
    """)
    job = cur.fetchone()
    if job:
        print(f"\nJob Verification: FOUND")
        print(f"  Proc: {job[1]}")
        print(f"  Interval: {job[2]}")
        print(f"  Next Run: {job[3]}")
    else:
        print("\nJob Verification: NOT FOUND")

    # 2. Performance Check
    print("\n--- Performance Check: Fetching Status for Map ---")
    start_time = time.time()
    cur.execute("SELECT * FROM public.estatus_estaciones")
    rows = cur.fetchall()
    end_time = time.time()
    
    duration_ms = (end_time - start_time) * 1000
    print(f"Query returned {len(rows)} stations in {duration_ms:.2f} ms")
    
    if duration_ms < 50:
        print("RESULT: FAST (Success)")
    else:
        print("RESULT: SLOW (Warning)")

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
