import pyodbc

conn_str = "Driver={ODBC Driver 18 for SQL Server};Server=atlas16.ddns.net;Database=IGSCLOUD;User Id=sa;Password=Atlas2025$$;TrustServerCertificate=yes;"

try:
    conn = pyodbc.connect(conn_str)
    cur = conn.cursor()
    
    print("Checking SQL Server Estacion table...")
    cur.execute("SELECT TOP 10 IdAsignado, Nombre FROM Estacion WHERE Visible = 1 AND Activo = 1")
    rows = cur.fetchall()
    for r in rows:
        print(f"  IdAsignado: {r[0]}, Nombre: {r[1]}")
        
except Exception as e:
    print(f"Error: {e}")
finally:
    if 'conn' in locals() and conn:
        conn.close()
