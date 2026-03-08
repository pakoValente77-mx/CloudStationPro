import psycopg2
import sys

SRC_HOST = "192.168.1.72"
SRC_USER = "postgres"
SRC_PASS = "Cfe123pass"
SRC_DB = "mycloud_timescale"

def dump_full():
    try:
        conn = psycopg2.connect(host=SRC_HOST, user=SRC_USER, password=SRC_PASS, dbname=SRC_DB)
        cur = conn.cursor()
        
        # 1. Functions and Procedures (Detailed)
        print("=== FUNCTIONS AND PROCEDURES ===")
        cur.execute("""
            SELECT p.proname, n.nspname
            FROM pg_proc p
            JOIN pg_namespace n ON p.pronamespace = n.oid
            WHERE n.nspname = 'public'
        """)
        funcs = cur.fetchall()
        for name, nsp in funcs:
            try:
                cur.execute(f"SELECT pg_get_functiondef(p.oid) FROM pg_proc p JOIN pg_namespace n ON p.pronamespace = n.oid WHERE p.proname = '{name}' AND n.nspname = '{nsp}'")
                print(f"\n--- OBJECT: {name} ---")
                print(cur.fetchone()[0])
            except Exception as fe:
                print(f"\n--- OBJECT: {name} (FAIL) ---")
                print(f"Error getting definition: {fe}")
                conn.rollback()

        # 2. Triggers
        print("\n=== TRIGGERS ===")
        cur.execute("""
            SELECT tgname, relname as table_name, pg_get_triggerdef(t.oid) as definition
            FROM pg_trigger t
            JOIN pg_class c ON t.tgrelid = c.oid
            JOIN pg_namespace n ON c.relnamespace = n.oid
            WHERE n.nspname = 'public' AND NOT tgisinternal
        """)
        trigs = cur.fetchall()
        for t in trigs:
            print(f"\n--- Trigger: {t[0]} on {t[1]} ---")
            print(t[2])

    except Exception as e:
        print(f"Global Error: {e}")
    finally:
        if 'conn' in locals(): conn.close()

if __name__ == "__main__":
    dump_full()
