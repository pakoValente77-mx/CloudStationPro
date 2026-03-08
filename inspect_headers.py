import psycopg2
from psycopg2 import sql

# Connection details
HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "Cfe123pass"
DBNAME = "mycloud_timescale" 

try:
    print(f"Connecting to {DBNAME} at {HOST}...")
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    cur = conn.cursor()
    
    table_name = 'dcp_headers'
    schema_name = 'public'
    
    print(f"\n--- Table: {schema_name}.{table_name} ---")
    
    # Get columns
    cur.execute("""
        SELECT column_name, data_type 
        FROM information_schema.columns 
        WHERE table_schema = %s AND table_name = %s
    """, (schema_name, table_name))
    columns = cur.fetchall()
    for col in columns:
        print(f"  {col[0]}: {col[1]}")
        
    # Get row count
    try:
            cur.execute(sql.SQL("SELECT count(*) FROM {}.{}").format(sql.Identifier(schema_name), sql.Identifier(table_name)))
            count = cur.fetchone()[0]
            print(f"  Row count: {count}")
    except Exception as e:
            print(f"  Could not count rows: {e}")

    # preview data
    cur.execute(f"SELECT * FROM {schema_name}.{table_name} LIMIT 3")
    rows = cur.fetchall()
    print("\nPreview data:")
    for row in rows:
        print(row)

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
