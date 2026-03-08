import psycopg2
from psycopg2 import sql

# Connection details
HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "***REDACTED-PG-PASSWORD***"
DBNAME = "postgres" 

try:
    print(f"Connecting to {HOST}...")
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    cur = conn.cursor()
    print("Connected successfully.")
    
    # List tables
    cur.execute("""
        SELECT table_name 
        FROM information_schema.tables 
        WHERE table_schema = 'public'
    """)
    tables = cur.fetchall()
    print(f"Found {len(tables)} tables in public schema:")
    
    for table in tables:
        table_name = table[0]
        print(f"\n--- Table: {table_name} ---")
        
        # Get columns
        cur.execute("""
            SELECT column_name, data_type 
            FROM information_schema.columns 
            WHERE table_name = %s
        """, (table_name,))
        columns = cur.fetchall()
        for col in columns:
            print(f"  {col[0]}: {col[1]}")
            
        # Get row count
        try:
             cur.execute(sql.SQL("SELECT count(*) FROM {}").format(sql.Identifier(table_name)))
             count = cur.fetchone()[0]
             print(f"  Row count: {count}")
        except Exception as e:
             print(f"  Could not count rows: {e}")

        # Get indexes
        cur.execute("""
            SELECT indexname, indexdef 
            FROM pg_indexes 
            WHERE tablename = %s
        """, (table_name,))
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
