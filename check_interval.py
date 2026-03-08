import psycopg2

# Connection details
HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "***REDACTED-PG-PASSWORD***"
DBNAME = "mycloud_timescale" 

try:
    print(f"Connecting to {DBNAME} at {HOST}...")
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    cur = conn.cursor()
    
    # Get a sample dcp_id with many records
    cur.execute("""
        SELECT dcp_id, count(*) as c 
        FROM bitacora_goes 
        GROUP BY dcp_id 
        ORDER BY c DESC 
        LIMIT 1
    """)
    top_station = cur.fetchone()
    
    if top_station:
        station_id = top_station[0]
        print(f"\nAnalyzing station: {station_id} (Total records: {top_station[1]})")
        
        # Get timestamps for this station, ordered by time
        cur.execute("""
            SELECT timestamp_utc 
            FROM bitacora_goes 
            WHERE dcp_id = %s 
            ORDER BY timestamp_utc DESC 
            LIMIT 20
        """, (station_id,))
        timestamps = cur.fetchall()
        
        print("\nLast 20 transmissions:")
        prev_ts = None
        for i, row in enumerate(timestamps):
            ts = row[0]
            diff_str = ""
            if prev_ts:
                diff = prev_ts - ts
                diff_str = f" (Diff: {diff})"
            print(f"  {i+1}: {ts}{diff_str}")
            prev_ts = ts
            
    else:
        print("No stations found in bitacora_goes.")

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
