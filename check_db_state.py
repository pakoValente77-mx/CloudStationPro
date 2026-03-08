import psycopg2
from psycopg2 import sql

# Connection details from apply_trigger.py
HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "***REDACTED-PG-PASSWORD***"
DBNAME = "mycloud_timescale" 

def check_db():
    try:
        print(f"Connecting to {DBNAME} at {HOST}...")
        conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
        cur = conn.cursor()
        print("Connected successfully.\n")
        
        # 1. Check if bitacora_goes table exists
        cur.execute("SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'bitacora_goes');")
        exists = cur.fetchone()[0]
        print(f"Table 'bitacora_goes' exists: {exists}")
        
        # 2. Check if estatus_estaciones table exists
        cur.execute("SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'estatus_estaciones');")
        exists = cur.fetchone()[0]
        print(f"Table 'estatus_estaciones' exists: {exists}")
        
        if exists:
            # Check row count
            cur.execute("SELECT count(*) FROM public.estatus_estaciones;")
            count = cur.fetchone()[0]
            print(f"  Row count: {count}")
            
            # Check some samples
            cur.execute("SELECT dcp_id, color_estatus, fecha_calculo FROM public.estatus_estaciones LIMIT 5;")
            samples = cur.fetchall()
            print("  Sample records:")
            for s in samples:
                print(f"    {s}")
        
        # 3. Check triggers on bitacora_goes
        cur.execute("""
            SELECT trigger_name, event_manipulation, action_statement, action_timing
            FROM information_schema.triggers
            WHERE event_object_table = 'bitacora_goes';
        """)
        triggers = cur.fetchall()
        print(f"\nTriggers on 'bitacora_goes':")
        for trig in triggers:
            print(f"  - {trig[0]} ({trig[3]} {trig[1]})")
            
        # 4. Check if the function exists
        cur.execute("""
            SELECT routine_name 
            FROM information_schema.routines 
            WHERE routine_name = 'trg_actualizar_estatus';
        """)
        routine = cur.fetchone()
        print(f"\nFunction 'trg_actualizar_estatus' exists: {routine is not None}")

    except Exception as e:
        print(f"Error: {e}")
    finally:
        if 'conn' in locals() and conn:
            conn.close()

if __name__ == "__main__":
    check_db()
