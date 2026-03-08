import psycopg2

HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "Cfe123pass"
DBNAME = "mycloud_timescale" 

def apply_last_values():
    try:
        print(f"Conectando a {DBNAME} en {HOST}...")
        conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
        conn.autocommit = True
        cur = conn.cursor()
        
        # 0. Limpieza (en caso de error previo)
        print("\nLimpiando tabla previa si existe...")
        cur.execute("DROP TABLE IF EXISTS public.ultimas_mediciones CASCADE;")

        # 1. Aplicar esquema y trigger
        with open("schema_ultimos_valores.sql", "r") as f:
            schema_sql = f.read()
        print("\nCreando tabla y trigger para ultimas_mediciones...")
        cur.execute(schema_sql)
        print("Esquema aplicado con éxito.")

        # 2. Carga inicial de datos
        with open("populate_last_values.sql", "r") as f:
            populate_sql = f.read()
        print("\nRealizando carga inicial de últimos valores...")
        cur.execute(populate_sql)
        print("Carga inicial completada.")
        
    except Exception as e:
        print(f"Error: {e}")
    finally:
        if 'conn' in locals() and conn:
            conn.close()

if __name__ == "__main__":
    apply_last_values()
