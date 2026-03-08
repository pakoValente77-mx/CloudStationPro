import psycopg2
import sys

SRC_HOST = "192.168.1.72"
SRC_USER = "postgres"
SRC_PASS = "Cfe123pass"
SRC_DB = "mycloud_timescale"

def dump_all_objects():
    try:
        conn = psycopg2.connect(host=SRC_HOST, user=SRC_USER, password=SRC_PASS, dbname=SRC_DB)
        cur = conn.cursor()
        
        # 1. Functions and Procedures
        print("\n=== FUNCTIONS AND PROCEDURES ===")
        cur.execute("""
            SELECT n.nspname as schema, p.proname as name, pg_get_functiondef(p.oid) as definition
            FROM pg_proc p
            JOIN pg_namespace n ON p.pronamespace = n.oid
            WHERE n.nspname = 'public'
        """)
        funcs = cur.fetchall()
        for f in funcs:
            print(f"\n--- {f[0]}.{f[1]} ---")
            print(f[2])

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

        # 3. Indices (Extra)
        print("\n=== INDEXES ===")
        cur.execute("""
            SELECT tablename, indexname, indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
        """)
        idxs = cur.fetchall()
        for i in idxs:
            print(f"\n--- Index: {i[1]} on {i[0]} ---")
            print(i[2])

    except Exception as e:
        print(f"Error: {e}")
    finally:
        if 'conn' in locals(): conn.close()

if __name__ == "__main__":
    dump_all_objects()
