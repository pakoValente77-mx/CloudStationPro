
import psycopg2

HOST = "atlas16.ddns.net"
USER = "postgres" 
PASSWORD = "Cfe123pass"
DBNAME = "postgres" 

try:
    conn = psycopg2.connect(host=HOST, user=USER, password=PASSWORD, dbname=DBNAME)
    cur = conn.cursor()
    
    # Fetch one row to see columns (cursor.description)
    cur.execute("SELECT * FROM public.resumen_horario LIMIT 1")
    col_names = [desc[0] for desc in cur.description]
    print(f"Columns in resumen_horario: {col_names}")

    cur.execute("SELECT * FROM public.resumen_diario LIMIT 1")
    col_names_daily = [desc[0] for desc in cur.description]
    print(f"Columns in resumen_diario: {col_names_daily}")

except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
