import psycopg2

HOST = "192.168.1.72"
USER = "postgres" 
PASSWORD = "***REDACTED-PG-PASSWORD***"
DBNAME = "mycloud_timescale" 

def check_schema():
    try:
        conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
        cur = conn.cursor()
        
        cur.execute("""
            SELECT column_name, data_type 
            FROM information_schema.columns 
            WHERE table_name = 'dcp_datos';
        """)
        cols = cur.fetchall()
        print("dcp_datos columns:")
        for c in cols:
            print(f"  {c[0]}: {c[1]}")
            
        cur.execute("""
            SELECT column_name, data_type 
            FROM information_schema.columns 
            WHERE table_name = 'resumen_horario';
        """)
        cols2 = cur.fetchall()
        print("\nresumen_horario columns:")
        for c in cols2:
            print(f"  {c[0]}: {c[1]}")

    except Exception as e:
        print(f"Error: {e}")
    finally:
        if 'conn' in locals() and conn:
            conn.close()

if __name__ == "__main__":
    check_schema()
