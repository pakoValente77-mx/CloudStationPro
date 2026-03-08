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
    
    # List Databases
    cur.execute("SELECT datname FROM pg_database WHERE datistemplate = false;")
    dbs = cur.fetchall()
    print(f"\nDatabases found ({len(dbs)}):")
    for db in dbs:
        print(f"  - {db[0]}")

    # List Schemas in current DB
    cur.execute("SELECT schema_name FROM information_schema.schemata;")
    schemas = cur.fetchall()
    print(f"\nSchemas in '{DBNAME}' ({len(schemas)}):")
    for s in schemas:
        print(f"  - {s[0]}")
        
    # Check tables in all schemas
    cur.execute("""
        SELECT table_schema, table_name 
        FROM information_schema.tables 
        WHERE table_schema NOT IN ('information_schema', 'pg_catalog')
    """)
    tables = cur.fetchall()
    print(f"\nTables found in '{DBNAME}' (excluding system schemas):")
    for t in tables:
        print(f"  - {t[0]}.{t[1]}")

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
