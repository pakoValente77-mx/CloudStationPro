import psycopg2
import time

try:
    conn = psycopg2.connect("host=192.168.1.72 dbname=mycloud_timescale user=postgres password=***REDACTED-PG-PASSWORD***")
    cur = conn.cursor()

    start = time.time()
    cur.execute("""
        SELECT dcp_id, date_trunc('hour', ts) as hour, count(*) as error_count
        FROM public.dcp_datos
        WHERE variable = 'nivel_de_agua' 
        AND ts >= now() - interval '24 hours'
        AND valido = false
        GROUP BY dcp_id, date_trunc('hour', ts);
    """)
    rows = cur.fetchall()
    end = time.time()
    
    print(f"Query returned {len(rows)} rows in {end - start:.3f} seconds.")

except Exception as e:
    print(e)
