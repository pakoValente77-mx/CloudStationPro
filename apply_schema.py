import psycopg2

# Connection details
HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "Cfe123pass"
DBNAME = "mycloud_timescale" 

try:
    print(f"Connecting to {DBNAME} at {HOST}...")
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    conn.autocommit = True # Procedures often require autocommit or transaction block control
    cur = conn.cursor()
    
    # Read SQL file
    with open("create_schema.sql", "r") as f:
        sql_commands = f.read()

    print("\nExecuting SQL commands to create table and procedure...")
    cur.execute(sql_commands)
    print("Schema created successfully.")
    
    print("\nRunning initial update (first calculation)...")
    cur.execute("CALL public.actualizar_estatus_estaciones();")
    print("Procedure executed.")

    # Verify results
    cur.execute("SELECT color_estatus, COUNT(*) FROM public.estatus_estaciones GROUP BY color_estatus")
    rows = cur.fetchall()
    print("\n--- Initial Status Counts ---")
    for row in rows:
        print(f"{row[0]}: {row[1]}")

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
