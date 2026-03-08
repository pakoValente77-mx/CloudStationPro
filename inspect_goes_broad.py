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
    
    # List Schemas
    cur.execute("SELECT schema_name FROM information_schema.schemata;")
    schemas = cur.fetchall()
    print(f"\nSchemas in '{DBNAME}':")
    for s in schemas:
        print(f"  - {s[0]}")

    # List first 100 tables
    cur.execute("""
        SELECT table_schema, table_name 
        FROM information_schema.tables 
        LIMIT 100
    """)
    tables = cur.fetchall()
    print(f"\nFirst 100 tables in '{DBNAME}':")
    for t in tables:
        print(f"  - {t[0]}.{t[1]}")

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
