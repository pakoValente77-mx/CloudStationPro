import psycopg2

# Connection details
HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "***REDACTED-PG-PASSWORD***"
DBNAME = "mycloud_timescale" 

try:
    print(f"Connecting to {DBNAME} at {HOST}...")
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    conn.autocommit = True
    cur = conn.cursor()
    
    # 1. Apply Schema (Procedure)
    with open("create_schema.sql", "r") as f:
        schema_sql = f.read()
    print("\nExecuting SQL to update Procedure...")
    cur.execute(schema_sql)
    print("Procedure updated successfully.")

    # 2. Apply Trigger
    with open("create_trigger.sql", "r") as f:
        trigger_sql = f.read()
    print("\nExecuting SQL to update Trigger...")
    cur.execute(trigger_sql)
    print("Trigger updated successfully.")
    
    # Optional: Run the procedure once to sync all stations
    print("\nRunning procedure to sync all stations...")
    cur.execute("CALL public.actualizar_estatus_estaciones();")
    print("Initial sync completed.")

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
