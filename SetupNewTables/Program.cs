// Run with: dotnet script setup_new_tables.csx
// Or: dotnet run (from a temp project)
// This creates the required tables for Early Warning, Chat, and Push Notifications

using System;
using System.Data;
using Microsoft.Data.SqlClient;

var server = args.Length > 0 && args[0] == "--prod" ? "127.0.0.1" : "atlas16.ddns.net";
var database = args.Length > 0 && args[0] == "--prod" ? "IGSCLOUD_PRO" : "IGSCLOUD";
var password = args.Length > 0 && args[0] == "--prod" ? "Cfe2026$$" : "Atlas2025$$";

Console.WriteLine($"Connecting to {server}/{database}...");

var connStr = $"Server={server};Database={database};User Id=sa;Password={password};TrustServerCertificate=True;";

using var conn = new SqlConnection(connStr);
conn.Open();
Console.WriteLine("Connected!");

void Exec(string sql, string label)
{
    using var cmd = new SqlCommand(sql, conn);
    cmd.ExecuteNonQuery();
    Console.WriteLine($"  [OK] {label}");
}

// 1. AlertRecord
Exec(@"
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AlertRecord')
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
    )", "AlertRecord table");

Exec(@"
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AlertRecord_Fecha')
    CREATE INDEX IX_AlertRecord_Fecha ON AlertRecord(FechaAlerta DESC)", "AlertRecord index");

// 2. ChatMessages
Exec(@"
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ChatMessages')
    CREATE TABLE ChatMessages (
        Id          UNIQUEIDENTIFIER PRIMARY KEY,
        ChatId      UNIQUEIDENTIFIER NOT NULL,
        Room        NVARCHAR(255) NOT NULL DEFAULT 'general',
        UserId      NVARCHAR(450) NOT NULL,
        UserName    NVARCHAR(256) NOT NULL,
        FullName    NVARCHAR(200),
        Message     NVARCHAR(MAX) NOT NULL,
        Timestamp   DATETIME2 NOT NULL
    )", "ChatMessages table");

Exec(@"
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ChatMessages_Room')
    CREATE INDEX IX_ChatMessages_Room ON ChatMessages(Room, Timestamp DESC)", "ChatMessages index");

// 3. DeviceTokens
Exec(@"
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DeviceTokens')
    CREATE TABLE DeviceTokens (
        Id          BIGINT IDENTITY(1,1) PRIMARY KEY,
        UserId      NVARCHAR(450) NOT NULL,
        Token       NVARCHAR(500) NOT NULL,
        Platform    NVARCHAR(20) NOT NULL DEFAULT 'android',
        CreatedAt   DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        LastSeen    DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    )", "DeviceTokens table");

Exec(@"
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DeviceTokens_UserToken')
    CREATE UNIQUE INDEX IX_DeviceTokens_UserToken ON DeviceTokens(UserId, Token)", "DeviceTokens index");

// Verify
using var verify = new SqlCommand(
    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('AlertRecord','ChatMessages','DeviceTokens') ORDER BY TABLE_NAME", conn);
using var reader = verify.ExecuteReader();
Console.WriteLine("\nTablas verificadas:");
while (reader.Read()) Console.WriteLine($"  ✓ {reader.GetString(0)}");

Console.WriteLine("\nDone!");
