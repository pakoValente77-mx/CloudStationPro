import pyodbc 

conn_str = (
    r'Driver={ODBC Driver 18 for SQL Server};'
    r'Server=localhost;'
    r'Database=CloudStationDB;'
    r'UID=sa;'
    r'PWD=MyP@ssword123;'
    r'TrustServerCertificate=yes;'
)

try:
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    cursor.execute("SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'GruposUsuario'")
    print("GruposUsuario:")
    for row in cursor:
        print(f"  {row.COLUMN_NAME} ({row.DATA_TYPE})")
        
    cursor.execute("SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'EstacionGrupoUsuario'")
    print("\nEstacionGrupoUsuario:")
    for row in cursor:
        print(f"  {row.COLUMN_NAME} ({row.DATA_TYPE})")
        
except Exception as e:
    print(e)
