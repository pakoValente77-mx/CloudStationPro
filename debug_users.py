import pyodbc
import sys

# Credenciales LOCALES (donde se migró)
CONN_STR = "Driver={ODBC Driver 17 for SQL Server};Server=127.0.0.1;Database=IGSCLOUD;UID=sa;PWD=Cfe2026##;TrustServerCertificate=yes;"

def check_users():
    try:
        conn = pyodbc.connect(CONN_STR)
        cur = conn.cursor()
        
        print("\n--- REVISIÓN DE USUARIOS MIGRADOS ---")
        cur.execute("""
            SELECT TOP 5 
                UserName, 
                NormalizedUserName, 
                Email, 
                NormalizedEmail, 
                PasswordHash, 
                SecurityStamp, 
                ConcurrencyStamp 
            FROM AspNetUsers
        """)
        
        rows = cur.fetchall()
        for row in rows:
            print(f"User: {row.UserName}")
            print(f"  - Normalized: {row.NormalizedUserName}")
            print(f"  - Email: {row.Email}")
            print(f"  - PasswordHash: {'SÍ' if row.PasswordHash else 'NO'}")
            print(f"  - SecurityStamp: {'SÍ' if row.SecurityStamp else 'FALTA'}")
            print(f"  - ConcurrencyStamp: {'SÍ' if row.ConcurrencyStamp else 'FALTA'}")
            print("-" * 20)
            
        cur.execute("SELECT COUNT(*) FROM AspNetUserRoles")
        print(f"Usuarios con roles asignados: {cur.fetchone()[0]}")
        
    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    check_users()
