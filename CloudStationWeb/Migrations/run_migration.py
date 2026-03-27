import pymssql

conn = pymssql.connect(
    server='atlas16.ddns.net',
    port=1433,
    user='sa',
    password='***REDACTED-SQL-PASSWORD***',
    database='IGSCLOUD',
    login_timeout=10
)
cur = conn.cursor()
cur.execute("""
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FunVasosReferencias')
BEGIN
    CREATE TABLE FunVasosReferencias (
        Id          BIGINT IDENTITY(1,1) PRIMARY KEY,
        PresaKey    NVARCHAR(100) NOT NULL,
        Nombre      NVARCHAR(200) NOT NULL,
        Valor       DECIMAL(18,4) NOT NULL,
        Color       NVARCHAR(20) DEFAULT '#ffff00',
        Visible     BIT DEFAULT 1,
        UsuarioModifica NVARCHAR(256),
        FechaCreacion   DATETIME2 DEFAULT GETDATE(),
        FechaModifica   DATETIME2 DEFAULT GETDATE()
    );
    CREATE INDEX IX_FunVasosReferencias_PresaKey ON FunVasosReferencias(PresaKey);
END
""")
conn.commit()

cur.execute("SELECT name FROM sys.tables WHERE name = 'FunVasosReferencias'")
row = cur.fetchone()
print('OK: Table FunVasosReferencias exists' if row else 'FAIL: Table not found')
conn.close()
