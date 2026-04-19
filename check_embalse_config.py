import pyodbc

conn = pyodbc.connect(
    "DRIVER={ODBC Driver 17 for SQL Server};"
    "SERVER=atlas16.ddns.net;DATABASE=IGSCLOUD;"
    "UID=sa;PWD=***REDACTED-SQL-PASSWORD***;TrustServerCertificate=yes"
)
cur = conn.cursor()

cur.execute("SELECT OBJECT_ID('EmbalseConfig','U')")
r = cur.fetchone()
if r[0] is None:
    print("TABLA NO EXISTE")
else:
    cur.execute("SELECT COUNT(*) FROM EmbalseConfig")
    cnt = cur.fetchone()[0]
    print(f"Total filas: {cnt}")
    if cnt > 0:
        cur.execute("""
            SELECT Id, PresaKey, NombreDisplay, 
                   CAST(Namo AS VARCHAR) Namo, CAST(Namino AS VARCHAR) Namino,
                   IsActive, Color, HydroKey, CuencaCode,
                   ExcelHeaderRow, ExcelDataStartRow, ExcelDataEndRow,
                   IsTaponType, TotalUnits, BhgKey
            FROM EmbalseConfig ORDER BY SortOrder
        """)
        cols = [d[0] for d in cur.description]
        print(" | ".join(cols))
        print("-" * 140)
        for row in cur.fetchall():
            print(" | ".join(str(v) for v in row))
    else:
        print("La tabla existe pero está VACÍA")

conn.close()
