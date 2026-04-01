-- =====================================================
-- SQL Server tables for Early Warning + Chat + Push
-- Run on IGSCLOUD (dev) and IGSCLOUD_PRO (production)
-- =====================================================

-- 1. Alert history (populated by EarlyWarningService background)
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
        Nivel           NVARCHAR(20),           -- 'CRÍTICA' | 'ADVERTENCIA'
        FechaAlerta     DATETIME2 NOT NULL,
        FechaEnvio      DATETIME2,
        CorreosEnviados INT DEFAULT 0,
        Enviada         BIT DEFAULT 0
    );

    CREATE INDEX IX_AlertRecord_Fecha ON AlertRecord(FechaAlerta DESC);
    CREATE INDEX IX_AlertRecord_Sensor ON AlertRecord(IdSensor);
    PRINT 'Created table: AlertRecord';
END
GO

-- 2. Chat messages (populated by SignalR ChatHub)
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
    );

    CREATE INDEX IX_ChatMessages_Room ON ChatMessages(Room, Timestamp DESC);
    CREATE INDEX IX_ChatMessages_User ON ChatMessages(UserId);
    PRINT 'Created table: ChatMessages';
END
GO

-- 3. Device tokens for push notifications (Firebase FCM)
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DeviceTokens')
BEGIN
    CREATE TABLE DeviceTokens (
        Id          BIGINT IDENTITY(1,1) PRIMARY KEY,
        UserId      NVARCHAR(450) NOT NULL,
        Token       NVARCHAR(500) NOT NULL,
        Platform    NVARCHAR(20) NOT NULL DEFAULT 'android',  -- 'android' | 'ios' | 'web'
        CreatedAt   DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        LastSeen    DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );

    CREATE UNIQUE INDEX IX_DeviceTokens_UserToken ON DeviceTokens(UserId, Token);
    PRINT 'Created table: DeviceTokens';
END
GO

PRINT '=== Setup complete ==='
