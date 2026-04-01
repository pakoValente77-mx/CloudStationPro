"""
Create new tables for Early Warning, Chat, and Push Notifications
on SQL Server (IGSCLOUD dev or IGSCLOUD_PRO production).
"""
import pyodbc
import sys

# Default: dev server
SERVER = "atlas16.ddns.net"
DATABASE = "IGSCLOUD"
USER = "sa"
PASSWORD = "Atlas2025$$"

if "--prod" in sys.argv:
    SERVER = "127.0.0.1"
    DATABASE = "IGSCLOUD_PRO"
    PASSWORD = "Cfe2026$$"
    print("*** PRODUCTION MODE ***")

print(f"Connecting to {SERVER}/{DATABASE}...")

conn = pyodbc.connect(
    f"DRIVER={{ODBC Driver 18 for SQL Server}};"
    f"SERVER={SERVER};DATABASE={DATABASE};UID={USER};PWD={PASSWORD};"
    f"TrustServerCertificate=yes;Encrypt=no;",
    autocommit=True
)
cursor = conn.cursor()

# ---- 1. AlertRecord ----
cursor.execute("""
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AlertRecord')
    BEGIN
        CREATE TABLE AlertRecord (
            Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
            IdSensor        UNIQUEIDENTIFIER NOT NULL,
            IdUmbral        BIGINT NOT NULL,
            NombreEstacion  NVARCHAR(200),
            NombreSensor    NVARCHAR(200),
            NombreUmbral    NVARCHAR(200),
            Variable        NVARCHAR(100),
            ValorMedido     DECIMAL(18,4) NOT NULL,
            ValorUmbral     DECIMAL(18,4) NOT NULL,
            Operador        NVARCHAR(10),
            Nivel           NVARCHAR(20),
            FechaAlerta     DATETIME2 NOT NULL,
            FechaEnvio      DATETIME2,
            CorreosEnviados INT DEFAULT 0,
            Enviada         BIT DEFAULT 0
        )
    END
""")
cursor.execute("""
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AlertRecord_Fecha')
        CREATE INDEX IX_AlertRecord_Fecha ON AlertRecord(FechaAlerta DESC)
""")
cursor.execute("""
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AlertRecord_Sensor')
        CREATE INDEX IX_AlertRecord_Sensor ON AlertRecord(IdSensor)
""")
print("  [OK] AlertRecord")

# ---- 2. ChatMessages ----
cursor.execute("""
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ChatMessages')
    BEGIN
        CREATE TABLE ChatMessages (
            Id          UNIQUEIDENTIFIER PRIMARY KEY,
            ChatId      UNIQUEIDENTIFIER NOT NULL,
            Room        NVARCHAR(255) NOT NULL DEFAULT 'general',
            UserId      NVARCHAR(450) NOT NULL,
            UserName    NVARCHAR(256) NOT NULL,
            FullName    NVARCHAR(200),
            Message     NVARCHAR(MAX) NOT NULL,
            Timestamp   DATETIME2 NOT NULL
        )
    END
""")
cursor.execute("""
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ChatMessages_Room')
        CREATE INDEX IX_ChatMessages_Room ON ChatMessages(Room, Timestamp DESC)
""")
cursor.execute("""
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ChatMessages_User')
        CREATE INDEX IX_ChatMessages_User ON ChatMessages(UserId)
""")
print("  [OK] ChatMessages")

# ---- 3. DeviceTokens ----
cursor.execute("""
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DeviceTokens')
    BEGIN
        CREATE TABLE DeviceTokens (
            Id          BIGINT IDENTITY(1,1) PRIMARY KEY,
            UserId      NVARCHAR(450) NOT NULL,
            Token       NVARCHAR(500) NOT NULL,
            Platform    NVARCHAR(20) NOT NULL DEFAULT 'android',
            CreatedAt   DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
            LastSeen    DATETIME2 NOT NULL DEFAULT GETUTCDATE()
        )
    END
""")
cursor.execute("""
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DeviceTokens_UserToken')
        CREATE UNIQUE INDEX IX_DeviceTokens_UserToken ON DeviceTokens(UserId, Token)
""")
print("  [OK] DeviceTokens")

# ---- Verify ----
cursor.execute("""
    SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES 
    WHERE TABLE_NAME IN ('AlertRecord', 'ChatMessages', 'DeviceTokens')
    ORDER BY TABLE_NAME
""")
tables = [row[0] for row in cursor.fetchall()]
print(f"\nTablas verificadas: {tables}")

conn.close()
print("Done!")
