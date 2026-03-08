import psycopg2

# Connection details
HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "Cfe123pass"
DBNAME = "mycloud_timescale" 

try:
    print(f"Connecting to {DBNAME} at {HOST}...")
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    conn.autocommit = True
    cur = conn.cursor()
    
    # 1. Check for pg_cron extension
    print("Checking for pg_cron extension...")
    cur.execute("SELECT 1 FROM pg_extension WHERE extname = 'pg_cron'")
    if cur.fetchone():
        print("pg_cron found. Scheduling job...")
        # Schedule every 15 minutes
        cur.execute("""
            SELECT cron.schedule('update_goes_status', '*/15 * * * *', 'CALL public.actualizar_estatus_estaciones()');
        """)
        print("Job scheduled via pg_cron.")
    else:
        print("pg_cron NOT found.")
        
        # 2. Check for TimescaleDB automation (User Defined Actions)
        print("Checking for TimescaleDB automation...")
        cur.execute("SELECT 1 FROM pg_extension WHERE extname = 'timescaledb'")
        if cur.fetchone():
             print("TimescaleDB found. Attempting to schedule User Defined Action...")
             try:
                 # Check if job already exists to avoid dupes
                 cur.execute("SELECT count(*) FROM timescaledb_information.jobs WHERE proc_name = 'actualizar_estatus_estaciones'")
                 if cur.fetchone()[0] == 0:
                     cur.execute("""
                         SELECT add_job('actualizar_estatus_estaciones', '15 minutes');
                     """)
                     print("Job scheduled via TimescaleDB User Defined Action.")
                 else:
                     print("TimescaleDB job already exists.")
             except Exception as ts_e:
                 print(f"Could not schedule via TimescaleDB: {ts_e}")
        else:
            print("No supported scheduler found in DB. You must use system CRON.")

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
