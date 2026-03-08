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
    
    # Simulate the query to get the latest status for ALL stations
    # Utilizing the existing index: (dcp_id, timestamp_utc DESC)
    query = """
        EXPLAIN ANALYZE
        SELECT DISTINCT ON (dcp_id) dcp_id, timestamp_utc, exito
        FROM bitacora_goes
        ORDER BY dcp_id, timestamp_utc DESC;
    """
    
    print("\nExecuting EXPLAIN ANALYZE for 'latest status' query...")
    cur.execute(query)
    plan = cur.fetchall()
    
    print("\n--- Query Plan ---")
    for row in plan:
        print(row[0])

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
