import psycopg2
from psycopg2 import sql

# Connection details
HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "Cfe123pass"
DBNAME = "monitor_goes" 

try:
    print(f"Connecting to {DBNAME} at {HOST}...")
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    cur = conn.cursor()
    print("Connected successfully.")
    
    # Check tables in user schemas
    cur.execute("""
        SELECT table_schema, table_name 
        FROM information_schema.tables 
        WHERE table_schema NOT IN (
            'information_schema', 'pg_catalog', 
            '_timescaledb_internal', '_timescaledb_cache', 
            '_timescaledb_config', '_timescaledb_catalog',
            'timescaledb_information', 'timescaledb_experimental'
        )
    """)
    tables = cur.fetchall()
    print(f"\nUser Tables found in '{DBNAME}':")
    
    for t in tables:
        schema = t[0]
        table_name = t[1]
        print(f"\n--- Table: {schema}.{table_name} ---")
        
        # Get columns
        cur.execute("""
            SELECT column_name, data_type 
            FROM information_schema.columns 
            WHERE table_schema = %s AND table_name = %s
        """, (schema, table_name))
        columns = cur.fetchall()
        for col in columns:
            print(f"  {col[0]}: {col[1]}")
            
        # Get row count
        try:
             cur.execute(sql.SQL("SELECT count(*) FROM {}.{}").format(sql.Identifier(schema), sql.Identifier(table_name)))
             count = cur.fetchone()[0]
             print(f"  Row count: {count}")
        except Exception as e:
             print(f"  Could not count rows: {e}")

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
