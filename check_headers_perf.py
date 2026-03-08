import psycopg2

# Connection details
HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "Cfe123pass"
DBNAME = "mycloud_timescale" 

try:
    print(f"Connecting to {DBNAME} at {HOST}...")
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    cur = conn.cursor()
    
    # 1. Inspect dcp_headers for interval info
    print("\n--- Inspecting dcp_headers columns ---")
    cur.execute("""
        SELECT column_name 
        FROM information_schema.columns 
        WHERE table_name = 'dcp_headers'
    """)
    cols = cur.fetchall()
    print([c[0] for c in cols])
    
    # Preview data to see if there's interval info
    print("\n--- Preview dcp_headers (Limit 3) ---")
    cur.execute("SELECT * FROM dcp_headers LIMIT 3")
    rows = cur.fetchall()
    for row in rows:
        print(row)

    # 2. Benchmark Count Query
    query = """
        EXPLAIN ANALYZE
        SELECT dcp_id, count(*) 
        FROM bitacora_goes 
        WHERE timestamp_utc > NOW() - INTERVAL '48 hours' 
        GROUP BY dcp_id;
    """
    print("\n--- Benchmarking Count Query ---")
    print("Executing EXPLAIN ANALYZE...")
    cur.execute(query)
    plan = cur.fetchall()
    for row in plan:
        print(row[0])

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
