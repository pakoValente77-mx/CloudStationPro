import psycopg2

HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "Cfe123pass"
DBNAME = "mycloud_timescale" 

def check_indices():
    try:
        conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
        cur = conn.cursor()
        
        print("Checking indices for public.bitacora_goes...")
        cur.execute("""
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE tablename = 'bitacora_goes' AND schemaname = 'public';
        """)
        indices = cur.fetchall()
        for idx in indices:
            print(f"Index: {idx[0]}")
            print(f"Def: {idx[1]}")
            
    except Exception as e:
        print(f"Error: {e}")
    finally:
        if 'conn' in locals() and conn:
            conn.close()

if __name__ == "__main__":
    check_indices()
