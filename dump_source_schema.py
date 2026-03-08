import psycopg2
import sys

SRC_HOST = "192.168.1.72"
SRC_USER = "postgres"
SRC_PASS = "***REDACTED-PG-PASSWORD***"
SRC_DB = "mycloud_timescale"

TABLES = ["dcp_headers", "dcp_datos", "resumen_horario", "resumen_diario", "bitacora_goes", "ultimas_mediciones", "estatus_estaciones"]

def dump_schema():
    try:
        conn = psycopg2.connect(host=SRC_HOST, user=SRC_USER, password=SRC_PASS, dbname=SRC_DB)
        cur = conn.cursor()
        
        for table in TABLES:
            print(f"\n--- TABLE: {table} ---")
            cur.execute(f"""
                SELECT column_name, data_type, character_maximum_length, is_nullable
                FROM information_schema.columns 
                WHERE table_name = '{table}' AND table_schema = 'public'
                ORDER BY ordinal_position
            """)
            cols = cur.fetchall()
            for col in cols:
                print(f"  {col[0]}: {col[1]}" + (f"({col[2]})" if col[2] else "") + (" NULL" if col[3] == "YES" else " NOT NULL"))
                
    except Exception as e:
        print(f"Error: {e}")
    finally:
        if 'conn' in locals(): conn.close()

if __name__ == "__main__":
    dump_schema()
