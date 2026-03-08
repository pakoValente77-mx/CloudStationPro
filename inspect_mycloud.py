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
    print("Connected successfully.")
    
    # Check for specific schema
    schema_name = 'mycloudall_timescale'
    
    # List tables in the specific schema
    cur.execute("""
        SELECT table_name 
        FROM information_schema.tables 
        WHERE table_schema = %s
    """, (schema_name,))
    tables = cur.fetchall()
    
    if not tables:
        print(f"\nNo tables found in schema '{schema_name}'. Checking 'public' instead...")
        cur.execute("""
            SELECT table_name 
            FROM information_schema.tables 
            WHERE table_schema = 'public'
        """)
        tables = cur.fetchall()
        schema_name = 'public'

    print(f"\nTables found in schema '{schema_name}':")
    
    for t in tables:
        table_name = t[0]
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
            
        # Get row count (approximate for speed if needed, but count(*) is fine for now)
        try:
             cur.execute(sql.SQL("SELECT count(*) FROM {}.{}").format(sql.Identifier(schema_name), sql.Identifier(table_name)))
             count = cur.fetchone()[0]
             print(f"  Row count: {count}")
        except Exception as e:
             print(f"  Could not count rows: {e}")

        # Get indexes
        cur.execute("""
            SELECT indexname, indexdef 
            FROM pg_indexes 
            WHERE schemaname = %s AND tablename = %s
        """, (schema_name, table_name))
        indexes = cur.fetchall()
        if indexes:
            print("  Indexes:")
            for idx in indexes:
                print(f"    {idx[0]}: {idx[1]}")

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
