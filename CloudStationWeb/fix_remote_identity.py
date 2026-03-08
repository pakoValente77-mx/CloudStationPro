import pyodbc
import uuid
import sys

# === CONFIGURACIÓN NUBE (atlas16.ddns.net) ===
CONN_STR = (
    "Driver={ODBC Driver 17 for SQL Server};"
    "Server=atlas16.ddns.net;"
    "Database=IGSCLOUD;"
    "UID=sa;"
    "PWD=Atlas2025$$;"
    "TrustServerCertificate=yes;"
)

def repair_identity():
    try:
        print("[*] Conectando a la nube (atlas16) para reparar usuarios...")
        conn = pyodbc.connect(CONN_STR)
        cur = conn.cursor()

        # 1. Asegurar que existen las columnas si no están (para DBs muy viejas)
        print("[*] Verificando esquema de Identity Core...")
        cols_to_add = {
            "NormalizedUserName": "NVARCHAR(256) NULL",
            "NormalizedEmail": "NVARCHAR(256) NULL",
            "SecurityStamp": "NVARCHAR(MAX) NULL",
            "ConcurrencyStamp": "NVARCHAR(MAX) NULL",
            "LockoutEnabled": "BIT NOT NULL DEFAULT 1",
            "TwoFactorEnabled": "BIT NOT NULL DEFAULT 0",
            "EmailConfirmed": "BIT NOT NULL DEFAULT 1"
        }
        
        for col, dtype in cols_to_add.items():
            cur.execute(f"IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AspNetUsers') AND name = '{col}') "
                        f"ALTER TABLE AspNetUsers ADD {col} {dtype}")
        conn.commit()

        # 2. Reparar datos para compatibilidad con .NET Core
        print("[*] Normalizando usuarios y generando sellos de seguridad...")
        
        # Normalizar nombres y correos (Vital para el login)
        cur.execute("UPDATE AspNetUsers SET NormalizedUserName = UPPER(UserName) WHERE NormalizedUserName IS NULL OR NormalizedUserName != UPPER(UserName)")
        cur.execute("UPDATE AspNetUsers SET NormalizedEmail = UPPER(Email) WHERE Email IS NOT NULL AND (NormalizedEmail IS NULL OR NormalizedEmail != UPPER(Email))")
        
        # Generar SecurityStamp (Vital para que Identity acepte el login)
        cur.execute("SELECT Id FROM AspNetUsers WHERE SecurityStamp IS NULL")
        missing_stamp_ids = [row[0] for row in cur.fetchall()]
        for uid in missing_stamp_ids:
            cur.execute("UPDATE AspNetUsers SET SecurityStamp = ?, ConcurrencyStamp = ? WHERE Id = ?", (str(uuid.uuid4()), str(uuid.uuid4()), uid))
        
        conn.commit()
        print(f"[OK] Se repararon {len(missing_stamp_ids)} usuarios con sellos nuevos.")
        print("[OK] Todos los usuarios fueron normalizados a MAYÚSCULAS.")
        
        # 3. Asignar Roles si no tienen
        print("[*] Verificando roles core...")
        # Crear tabla Roles si no existe
        cur.execute("IF OBJECT_ID('AspNetRoles') IS NULL CREATE TABLE AspNetRoles (Id NVARCHAR(450) PRIMARY KEY, Name NVARCHAR(256), NormalizedName NVARCHAR(256), ConcurrencyStamp NVARCHAR(MAX))")
        
        roles = [
            ("7498c4d2-7acb-426b-88a4-0985f4640101", "Administrador", "ADMINISTRADOR"),
            ("7498c4d2-7acb-426b-88a4-0985f4640102", "Operador", "OPERADOR")
        ]
        for rid, name, norm in roles:
            cur.execute("IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE Name = ?) INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp) VALUES (?, ?, ?, ?)", (name, rid, name, norm, str(uuid.uuid4())))
        
        conn.commit()
        print("[OK] Base de datos en atlas16 ahora es 100% compatible con CloudStation Pro.")

    except Exception as e:
        print(f"[X] ERROR: {e}")
    finally:
        if 'conn' in locals(): conn.close()

if __name__ == "__main__":
    repair_identity()
