import pyodbc

# Usar la misma cadena de conexión que está en appsettings.json
conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=atlas16.ddns.net;Database=IGSCLOUD;User Id=sa;Password=***REDACTED-SQL-PASSWORD***;TrustServerCertificate=yes;"

try:
    conn = pyodbc.connect(conn_str)
    cur = conn.cursor()
    
    print("=== Estructura de la tabla Estacion ===")
    cur.execute("""
        SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = 'Estacion'
        ORDER BY ORDINAL_POSITION
    """)
    
    print("\nColumnas disponibles:")
    for row in cur.fetchall():
        col_name, data_type, max_len = row
        print(f"  {col_name}: {data_type}" + (f"({max_len})" if max_len else ""))
    
    print("\n=== Datos de ejemplo con Cuenca/Subcuenca ===")
    cur.execute("""
        SELECT TOP 5 IdAsignado, Nombre, 
               COLUMN_NAME as ColumnName
        FROM Estacion e
        CROSS APPLY (
            SELECT COLUMN_NAME 
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_NAME = 'Estacion' 
            AND (COLUMN_NAME LIKE '%cuenca%' OR COLUMN_NAME LIKE '%Cuenca%')
        ) cols
        WHERE Visible = 1 AND Activo = 1
    """)
    
    for row in cur.fetchall():
        print(f"  {row}")
        
    print("\n=== Buscando columnas con 'cuenca' (case-insensitive) ===")
    cur.execute("""
        SELECT COLUMN_NAME
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = 'Estacion'
        AND LOWER(COLUMN_NAME) LIKE '%cuenca%'
    """)
    
    cuenca_cols = cur.fetchall()
    if cuenca_cols:
        print("Columnas encontradas:")
        for col in cuenca_cols:
            print(f"  - {col[0]}")
            
        # Mostrar datos de ejemplo
        col_names = [col[0] for col in cuenca_cols]
        query = f"SELECT TOP 10 IdAsignado, Nombre, {', '.join(col_names)} FROM Estacion WHERE Visible = 1 AND Activo = 1"
        print(f"\n=== Datos de ejemplo ===")
        print(f"Query: {query}")
        cur.execute(query)
        for row in cur.fetchall():
            print(f"  {row}")
    else:
        print("No se encontraron columnas con 'cuenca'")
        
except Exception as e:
    print(f"Error: {e}")
    import traceback
    traceback.print_exc()
finally:
    if 'conn' in locals() and conn:
        conn.close()
