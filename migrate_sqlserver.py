import pyodbc
import sys
import uuid
import json
import os

# === CONFIGURACIÓN ===
SRC_CONN = (
    "Driver={ODBC Driver 17 for SQL Server};"
    "Server=atlas16.ddns.net;"
    "Database=IGSCLOUD;"
    "UID=sa;"
    "PWD=***REDACTED-SQL-PASSWORD***;"
    "TrustServerCertificate=yes;"
)

DST_CONN_BASE = (
    "Driver={ODBC Driver 17 for SQL Server};"
    "Server=127.0.0.1;"
    "UID=sa;"
    "PWD=Cfe2026##;"
    "TrustServerCertificate=yes;"
)
DB_NAME = "IGSCLOUD"

def migrate():
    try:
        print(f"[*] Conectando al ORIGEN (Remoto)...")
        src_conn = pyodbc.connect(SRC_CONN)
        src_cur = src_conn.cursor()

        print(f"[*] Conectando al DESTINO (Local - master)...")
        dst_conn_master = pyodbc.connect(DST_CONN_BASE + "Database=master;")
        dst_conn_master.autocommit = True
        dst_cur_master = dst_conn_master.cursor()

        # 1. Resetear Base de Datos Local
        print(f"[*] REINICIANDO BASE DE DATOS LOCAL '{DB_NAME}'...")
        dst_cur_master.execute(f"""
            IF EXISTS (SELECT name FROM sys.databases WHERE name = '{DB_NAME}')
            BEGIN
                ALTER DATABASE [{DB_NAME}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{DB_NAME}];
            END
        """)
        dst_cur_master.execute(f"CREATE DATABASE [{DB_NAME}]")
        print(f"[+] Base de datos local creada limpia.")
        
        dst_cur_master.close()
        dst_conn_master.close()

        # Conectar a la base de datos limpia
        dst_conn = pyodbc.connect(DST_CONN_BASE + f"Database={DB_NAME};")
        dst_cur = dst_conn.cursor()

        # 2. Configurar Roles del Nuevo Esquema
        print("[*] Configurando roles de ASP.NET Core...")
        roles_core = [
            ("7498c4d2-7acb-426b-88a4-0985f4640101", "Administrador", "ADMINISTRADOR"),
            ("7498c4d2-7acb-426b-88a4-0985f4640102", "Operador", "OPERADOR"),
            ("7498c4d2-7acb-426b-88a4-0985f4640103", "Visualizador", "VISUALIZADOR")
        ]
        
        dst_cur.execute("""
            CREATE TABLE [dbo].[AspNetRoles] (
                [Id] NVARCHAR(450) NOT NULL PRIMARY KEY,
                [Name] NVARCHAR(256) NULL,
                [NormalizedName] NVARCHAR(256) NULL,
                [ConcurrencyStamp] NVARCHAR(MAX) NULL
            )
        """)
        
        for r_id, r_name, r_norm in roles_core:
            dst_cur.execute("INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp) VALUES (?, ?, ?, ?)",
                           (r_id, r_name, r_norm, str(uuid.uuid4())))
        dst_conn.commit()

        # 3. Obtener lista de tablas (Excluyendo tablas de Identity para migrarlas manualmente)
        src_cur.execute("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'")
        tables = [row.TABLE_NAME for row in src_cur.fetchall()]
        
        skip_tables = ["AspNetRoles", "AspNetUserRoles", "AspNetUserLogins", "AspNetUserClaims", "AspNetRoleClaims", "__EFMigrationsHistory"]
        tables = [t for t in tables if t not in skip_tables]

        # Prioridad de carga
        priority = ["Cuenca", "Subcuenca", "TipoSensor", "Estacion", "Sensor", "DatosGOES", "AspNetUsers"]
        tables.sort(key=lambda x: priority.index(x) if x in priority else 99)

        for table in tables:
            print(f"\n[+] Migrando tabla: {table}")
            
            # Obtener esquema detallado
            src_cur.execute(f"""
                SELECT c.name, TYPE_NAME(c.system_type_id) AS type, c.max_length, c.is_nullable, c.is_identity
                FROM sys.columns c WHERE c.object_id = OBJECT_ID('{table}') ORDER BY c.column_id
            """)
            cols_info = src_cur.fetchall()
            src_cols = [c[0] for c in cols_info]
            
            create_cols = []
            for col in cols_info:
                name, dtype, length, nullable, is_identity = col
                if dtype in ['nvarchar', 'nchar']: length = length // 2
                type_str = dtype
                if dtype in ['varchar', 'nvarchar', 'char', 'nchar', 'varbinary']:
                    type_str += "(MAX)" if length == -1 or length > 8000 else f"({length})"
                
                null_str = "NULL" if nullable else "NOT NULL"
                identity_str = "IDENTITY(1,1)" if is_identity else ""
                create_cols.append(f"[{name}] {type_str} {identity_str} {null_str}")

            dst_cur.execute(f"CREATE TABLE [dbo].[{table}] ({', '.join(create_cols)})")
            dst_conn.commit()

            src_cur.execute(f"SELECT * FROM [{table}]")
            rows = src_cur.fetchall()
            
            if rows:
                print(f"    [*] Transfiriendo {len(rows)} registros...")
                
                # Manejo de Identity
                dst_cur.execute(f"SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('{table}') AND is_identity = 1")
                has_identity = dst_cur.fetchone() is not None
                if has_identity: dst_cur.execute(f"SET IDENTITY_INSERT [{table}] ON")

                if table == "AspNetUsers":
                    # Mapeo Crítico para Identity Core
                    dst_cur.execute(f"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}'")
                    dst_cols = [r[0] for r in dst_cur.fetchall()]
                    
                    final_rows = []
                    migrated_ids = []
                    for row in rows:
                        rd = dict(zip(src_cols, row))
                        nr = []
                        for dc in dst_cols:
                            if dc in rd and rd[dc] is not None: nr.append(rd[dc])
                            elif dc == "NormalizedUserName": nr.append(rd["UserName"].upper())
                            elif dc == "NormalizedEmail": nr.append(rd["Email"].upper() if rd.get("Email") else None)
                            elif dc == "SecurityStamp": nr.append(rd.get("SecurityStamp") or str(uuid.uuid4()))
                            elif dc == "ConcurrencyStamp": nr.append(str(uuid.uuid4()))
                            elif dc == "LockoutEnabled": nr.append(True)
                            elif dc == "TwoFactorEnabled": nr.append(False)
                            elif dc == "EmailConfirmed": nr.append(True)
                            elif dc == "AccessFailedCount": nr.append(0)
                            # Custom ApplicationUser
                            elif dc == "FullName": nr.append(rd.get("FullName") or rd["UserName"])
                            elif dc == "IsActive": nr.append(True)
                            elif dc == "CreatedAt": nr.append(rd.get("CreatedAt") or '2026-01-01')
                            else: nr.append(None)
                        final_rows.append(tuple(nr))
                        migrated_ids.append(rd["Id"])

                    ph = ", ".join(["?" for _ in dst_cols])
                    tc = ", ".join([f"[{c}]" for c in dst_cols])
                    dst_cur.executemany(f"INSERT INTO [{table}] ({tc}) VALUES ({ph})", final_rows)

                    # Asignar Rol Operador
                    print(f"    [*] Asignando rol de 'Operador' a todos...")
                    dst_cur.execute("CREATE TABLE [dbo].[AspNetUserRoles] ([UserId] NVARCHAR(450) NOT NULL, [RoleId] NVARCHAR(450) NOT NULL, PRIMARY KEY ([UserId], [RoleId]))")
                    rid = "7498c4d2-7acb-426b-88a4-0985f4640102"
                    dst_cur.executemany("INSERT INTO AspNetUserRoles (UserId, RoleId) VALUES (?, ?)", [(uid, rid) for uid in migrated_ids])
                else:
                    ph = ", ".join(["?" for _ in src_cols])
                    tc = ", ".join([f"[{c}]" for c in src_cols])
                    dst_cur.executemany(f"INSERT INTO [{table}] ({tc}) VALUES ({ph})", [tuple(r) for r in rows])

                if has_identity: dst_cur.execute(f"SET IDENTITY_INSERT [{table}] OFF")
                print(f"    [OK] Listo.")
            
            dst_conn.commit()

        print("\n" + "="*50)
        print(" MIGRACIÓN COMPLETADA EXITOSAMENTE ")
        print(" (Usuarios migrados y listos para login local) ")
        print("="*50)

    except Exception as e:
        print(f"\n[X] ERROR: {e}")
        import traceback
        traceback.print_exc()
    finally:
        if 'src_conn' in locals(): src_conn.close()
        if 'dst_conn' in locals(): dst_conn.close()

if __name__ == "__main__":
    migrate()
