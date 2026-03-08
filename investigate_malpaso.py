import psycopg2
import pyodbc

# Primero buscar en SQL Server
try:
    conn_sql = pyodbc.connect(
        'DRIVER={ODBC Driver 18 for SQL Server};'
        'SERVER=atlas16.ddns.net;'
        'DATABASE=IGSCLOUD;'
        'UID=sa;'
        'PWD=***REDACTED-SQL-PASSWORD***;'
        'TrustServerCertificate=yes;'
    )
    
    cursor = conn_sql.cursor()
    cursor.execute("""
        SELECT IdAsignado, Nombre, Latitud, Longitud, Visible, Activo
        FROM Estacion
        WHERE Nombre LIKE '%malpaso%'
    """)
    
    print("=== SQL Server - Estaciones Malpaso ===")
    for row in cursor.fetchall():
        print(f"ID: {row[0]}, Nombre: {row[1]}, Lat: {row[2]}, Lon: {row[3]}, Visible: {row[4]}, Activo: {row[5]}")
    
    cursor.close()
    conn_sql.close()
    
except Exception as e:
    print(f"Error SQL Server: {e}")

# Luego buscar en PostgreSQL
try:
    conn_pg = psycopg2.connect(
        host='atlas16.ddns.net',
        database='mycloud_timescale',
        user='postgres',
        password='***REDACTED-PG-PASSWORD***'
    )
    
    cur = conn_pg.cursor()
    
    # Buscar en resumen_horario
    cur.execute("""
        SELECT DISTINCT id_asignado, dcp_id
        FROM resumen_horario
        WHERE id_asignado LIKE '%MALPASO%'
        LIMIT 5
    """)
    
    print("\n=== PostgreSQL - Mapeo en resumen_horario ===")
    for row in cur.fetchall():
        print(f"ID Asignado: {row[0]}, DCP ID: {row[1]}")
    
    # Buscar últimas mediciones
    cur.execute("""
        SELECT dcp_id, variable, valor, ts
        FROM ultimas_mediciones
        WHERE dcp_id IN (
            SELECT DISTINCT dcp_id 
            FROM resumen_horario 
            WHERE id_asignado LIKE '%MALPASO%'
        )
        ORDER BY ts DESC
    """)
    
    print("\n=== PostgreSQL - Últimas mediciones ===")
    for row in cur.fetchall():
        print(f"DCP: {row[0]}, Variable: {row[1]}, Valor: {row[2]}, TS: {row[3]}")
    
    cur.close()
    conn_pg.close()
    
except Exception as e:
    print(f"Error PostgreSQL: {e}")
