import psycopg2
import sys

SRC_HOST = "192.168.1.72"
SRC_USER = "postgres"
SRC_PASS = "Cfe123pass"
SRC_DB = "mycloud_timescale"

TABLES = ["dcp_headers", "dcp_datos", "resumen_horario", "resumen_diario", "bitacora_goes", "ultimas_mediciones", "estatus_estaciones"]

def dump_constraints():
    try:
        conn = psycopg2.connect(host=SRC_HOST, user=SRC_USER, password=SRC_PASS, dbname=SRC_DB)
        cur = conn.cursor()
        
        for table in TABLES:
            print(f"\n=== CONSTRAINTS FOR: {table} ===")
            
            # 1. Primary Keys
            cur.execute(f"""
                SELECT
                    conname as constraint_name,
                    pg_get_constraintdef(c.oid) as constraint_def
                FROM pg_constraint c
                JOIN pg_namespace n ON n.oid = c.connamespace
                JOIN pg_class t ON t.oid = c.conrelid
                WHERE n.nspname = 'public' AND t.relname = '{table}' AND c.contype = 'p'
            """)
            pks = cur.fetchall()
            for pk in pks:
                print(f"  PRIMARY KEY: {pk[0]} -> {pk[1]}")

            # 2. Unique Constraints
            cur.execute(f"""
                SELECT
                    conname as constraint_name,
                    pg_get_constraintdef(c.oid) as constraint_def
                FROM pg_constraint c
                JOIN pg_namespace n ON n.oid = c.connamespace
                JOIN pg_class t ON t.oid = c.conrelid
                WHERE n.nspname = 'public' AND t.relname = '{table}' AND c.contype = 'u'
            """)
            uniques = cur.fetchall()
            for u in uniques:
                print(f"  UNIQUE: {u[0]} -> {u[1]}")

            # 3. Unique Indexes (Often used for ON CONFLICT instead of constraints)
            cur.execute(f"""
                SELECT
                    i.relname as index_name,
                    pg_get_indexdef(idx.indexrelid) as index_def
                FROM pg_index idx
                JOIN pg_class t ON t.oid = idx.indrelid
                JOIN pg_class i ON i.oid = idx.indexrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname = 'public' AND t.relname = '{table}' AND idx.indisunique = true
            """)
            unique_idxs = cur.fetchall()
            for ui in unique_idxs:
                print(f"  UNIQUE INDEX: {ui[0]} -> {ui[1]}")
                
    except Exception as e:
        print(f"Error: {e}")
    finally:
        if 'conn' in locals(): conn.close()

if __name__ == "__main__":
    dump_constraints()
