
import psycopg2
import sys

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
    
    tables_to_check = ["dcp_datos", "resumen_horario", "resumen_diario"]

    for table in tables_to_check:
        print(f"\nScanning table: {table}")
        cur.execute(f"""
            SELECT column_name, data_type 
            FROM information_schema.columns 
            WHERE table_schema = 'public' 
            AND table_name = '{table}'
        """)
        cols = cur.fetchall()
        if not cols:
            print("  [Use schema search if public fails]")
            # Fallback if not in public
        else:
            for col in cols:
                print(f"  - {col[0]} ({col[1]})")

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
