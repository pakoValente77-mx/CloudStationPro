import pyodbc

SRC_CONN = "Driver={ODBC Driver 17 for SQL Server};Server=atlas16.ddns.net;Database=IGSCLOUD;UID=sa;PWD=Atlas2025$$;TrustServerCertificate=yes;"

def dump_identity_schema():
    try:
        conn = pyodbc.connect(SRC_CONN)
        cur = conn.cursor()
        
        tables = ["AspNetUsers", "AspNetRoles", "AspNetUserRoles", "AspNetUserClaims", "AspNetUserLogins"]
        
        for table in tables:
            print(f"\n--- TABLE: {table} ---")
            try:
                cur.execute(f"SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' ORDER BY ORDINAL_POSITION")
                cols = cur.fetchall()
                if not cols:
                    print("Table not found.")
                for col in cols:
                    print(f"  {col[0]}: {col[1]}")
            except Exception as e:
                print(f"Error checking {table}: {e}")
                
    except Exception as e:
        print(f"Connection Error: {e}")

if __name__ == "__main__":
    dump_identity_schema()
