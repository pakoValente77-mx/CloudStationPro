-- =====================================================================
-- CLOUDSTATION PIH - Script COMPLETO de Despliegue a Producción
-- Base de datos: IGSCLOUD (SQL Server)
-- Fecha: 2026-03-31
-- Descripción: Script completo desde cero - Identity, Documentos,
--              Usuarios, Roles, Tablas nuevas, Seeds
-- INSTRUCCIONES: 
--   1. Hacer BACKUP de IGSCLOUD antes de ejecutar
--   2. Ejecutar con SSMS o sqlcmd contra IGSCLOUD
--   3. Cada bloque es idempotente (puede re-ejecutarse sin daño)
-- =====================================================================

USE IGSCLOUD;
GO

PRINT '================================================================'
PRINT ' CLOUDSTATION PIH - DEPLOY PRODUCCIÓN v2 (COMPLETO)'
PRINT ' Fecha: ' + CONVERT(VARCHAR, GETDATE(), 120)
PRINT '================================================================'
PRINT ''

-- =============================================================================
-- PARTE A: TABLAS ASP.NET IDENTITY (Migración 1: InitialIdentity)
-- =============================================================================

-- =====================================================================
-- A1. AspNetRoles
-- =====================================================================
PRINT '>> A1. AspNetRoles...'
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AspNetRoles')
BEGIN
    CREATE TABLE AspNetRoles (
        Id                  NVARCHAR(450) NOT NULL PRIMARY KEY,
        [Name]              NVARCHAR(256) NULL,
        NormalizedName      NVARCHAR(256) NULL,
        ConcurrencyStamp    NVARCHAR(MAX) NULL
    );
    CREATE UNIQUE NONCLUSTERED INDEX RoleNameIndex 
        ON AspNetRoles(NormalizedName) WHERE NormalizedName IS NOT NULL;
    PRINT '   + AspNetRoles creada'
END
ELSE PRINT '   = AspNetRoles ya existe'
GO

-- =====================================================================
-- A2. AspNetUsers (con TODOS los campos custom)
-- =====================================================================
PRINT '>> A2. AspNetUsers...'
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AspNetUsers')
BEGIN
    CREATE TABLE AspNetUsers (
        Id                      NVARCHAR(450) NOT NULL PRIMARY KEY,
        FullName                NVARCHAR(200) NOT NULL,
        CreatedAt               DATETIME2 NOT NULL,
        IsActive                BIT NOT NULL,
        UserName                NVARCHAR(256) NULL,
        NormalizedUserName      NVARCHAR(256) NULL,
        Email                   NVARCHAR(256) NULL,
        NormalizedEmail         NVARCHAR(256) NULL,
        EmailConfirmed          BIT NOT NULL,
        PasswordHash            NVARCHAR(MAX) NULL,
        SecurityStamp           NVARCHAR(MAX) NULL,
        ConcurrencyStamp        NVARCHAR(MAX) NULL,
        PhoneNumber             NVARCHAR(MAX) NULL,
        PhoneNumberConfirmed    BIT NOT NULL,
        TwoFactorEnabled        BIT NOT NULL,
        LockoutEnd              DATETIMEOFFSET NULL,
        LockoutEnabled          BIT NOT NULL,
        AccessFailedCount       INT NOT NULL
    );
    CREATE INDEX EmailIndex ON AspNetUsers(NormalizedEmail);
    CREATE UNIQUE NONCLUSTERED INDEX UserNameIndex 
        ON AspNetUsers(NormalizedUserName) WHERE NormalizedUserName IS NOT NULL;
    PRINT '   + AspNetUsers creada'
END
ELSE PRINT '   = AspNetUsers ya existe'
GO

-- =====================================================================
-- A3. AspNetRoleClaims
-- =====================================================================
PRINT '>> A3. AspNetRoleClaims...'
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AspNetRoleClaims')
BEGIN
    CREATE TABLE AspNetRoleClaims (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RoleId      NVARCHAR(450) NOT NULL,
        ClaimType   NVARCHAR(MAX) NULL,
        ClaimValue  NVARCHAR(MAX) NULL,
        CONSTRAINT FK_AspNetRoleClaims_AspNetRoles_RoleId 
            FOREIGN KEY (RoleId) REFERENCES AspNetRoles(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_AspNetRoleClaims_RoleId ON AspNetRoleClaims(RoleId);
    PRINT '   + AspNetRoleClaims creada'
END
ELSE PRINT '   = AspNetRoleClaims ya existe'
GO

-- =====================================================================
-- A4. AspNetUserClaims
-- =====================================================================
PRINT '>> A4. AspNetUserClaims...'
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AspNetUserClaims')
BEGIN
    CREATE TABLE AspNetUserClaims (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId      NVARCHAR(450) NOT NULL,
        ClaimType   NVARCHAR(MAX) NULL,
        ClaimValue  NVARCHAR(MAX) NULL,
        CONSTRAINT FK_AspNetUserClaims_AspNetUsers_UserId 
            FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_AspNetUserClaims_UserId ON AspNetUserClaims(UserId);
    PRINT '   + AspNetUserClaims creada'
END
ELSE PRINT '   = AspNetUserClaims ya existe'
GO

-- =====================================================================
-- A5. AspNetUserLogins
-- =====================================================================
PRINT '>> A5. AspNetUserLogins...'
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AspNetUserLogins')
BEGIN
    CREATE TABLE AspNetUserLogins (
        LoginProvider       NVARCHAR(450) NOT NULL,
        ProviderKey         NVARCHAR(450) NOT NULL,
        ProviderDisplayName NVARCHAR(MAX) NULL,
        UserId              NVARCHAR(450) NOT NULL,
        CONSTRAINT PK_AspNetUserLogins PRIMARY KEY (LoginProvider, ProviderKey),
        CONSTRAINT FK_AspNetUserLogins_AspNetUsers_UserId 
            FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_AspNetUserLogins_UserId ON AspNetUserLogins(UserId);
    PRINT '   + AspNetUserLogins creada'
END
ELSE PRINT '   = AspNetUserLogins ya existe'
GO

-- =====================================================================
-- A6. AspNetUserRoles
-- =====================================================================
PRINT '>> A6. AspNetUserRoles...'
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AspNetUserRoles')
BEGIN
    CREATE TABLE AspNetUserRoles (
        UserId  NVARCHAR(450) NOT NULL,
        RoleId  NVARCHAR(450) NOT NULL,
        CONSTRAINT PK_AspNetUserRoles PRIMARY KEY (UserId, RoleId),
        CONSTRAINT FK_AspNetUserRoles_AspNetRoles_RoleId 
            FOREIGN KEY (RoleId) REFERENCES AspNetRoles(Id) ON DELETE CASCADE,
        CONSTRAINT FK_AspNetUserRoles_AspNetUsers_UserId 
            FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_AspNetUserRoles_RoleId ON AspNetUserRoles(RoleId);
    PRINT '   + AspNetUserRoles creada'
END
ELSE PRINT '   = AspNetUserRoles ya existe'
GO

-- =====================================================================
-- A7. AspNetUserTokens
-- =====================================================================
PRINT '>> A7. AspNetUserTokens...'
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AspNetUserTokens')
BEGIN
    CREATE TABLE AspNetUserTokens (
        UserId          NVARCHAR(450) NOT NULL,
        LoginProvider   NVARCHAR(450) NOT NULL,
        [Name]          NVARCHAR(450) NOT NULL,
        [Value]         NVARCHAR(MAX) NULL,
        CONSTRAINT PK_AspNetUserTokens PRIMARY KEY (UserId, LoginProvider, [Name]),
        CONSTRAINT FK_AspNetUserTokens_AspNetUsers_UserId 
            FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
    );
    PRINT '   + AspNetUserTokens creada'
END
ELSE PRINT '   = AspNetUserTokens ya existe'
GO

-- =====================================================================
-- A8. __EFMigrationsHistory (requerida por EF Core)
-- =====================================================================
PRINT '>> A8. __EFMigrationsHistory...'
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '__EFMigrationsHistory')
BEGIN
    CREATE TABLE __EFMigrationsHistory (
        MigrationId     NVARCHAR(150) NOT NULL PRIMARY KEY,
        ProductVersion  NVARCHAR(32) NOT NULL
    );
    PRINT '   + __EFMigrationsHistory creada'
END
ELSE PRINT '   = __EFMigrationsHistory ya existe'
GO

-- =============================================================================
-- PARTE B: REPOSITORIO DE DOCUMENTOS (Migraciones 2, 3, 4)
-- =============================================================================

-- =====================================================================
-- B1. DocumentProducts
-- =====================================================================
PRINT '>> B1. DocumentProducts...'
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DocumentProducts')
BEGIN
    CREATE TABLE DocumentProducts (
        Id              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Name]          NVARCHAR(200) NOT NULL,
        Code            NVARCHAR(50) NOT NULL,
        [Description]   NVARCHAR(500) NULL,
        StoragePath     NVARCHAR(1000) NOT NULL DEFAULT '',
        FilePrefix      NVARCHAR(20) NOT NULL DEFAULT '',
        IsActive        BIT NOT NULL,
        RequiredDaily   BIT NOT NULL,
        CreatedAt       DATETIME2 NOT NULL
    );
    CREATE UNIQUE INDEX IX_DocumentProducts_Code ON DocumentProducts(Code);
    PRINT '   + DocumentProducts creada'
END
ELSE
BEGIN
    -- Agregar columnas de migraciones 3 y 4 si faltan
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('DocumentProducts') AND name = 'StoragePath')
    BEGIN
        ALTER TABLE DocumentProducts ADD StoragePath NVARCHAR(1000) NOT NULL DEFAULT '';
        PRINT '   + StoragePath agregada'
    END
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('DocumentProducts') AND name = 'FilePrefix')
    BEGIN
        ALTER TABLE DocumentProducts ADD FilePrefix NVARCHAR(20) NOT NULL DEFAULT '';
        PRINT '   + FilePrefix agregada'
    END
    PRINT '   = DocumentProducts ya existe (columnas verificadas)'
END
GO

-- =====================================================================
-- B2. DocumentEntries
-- =====================================================================
PRINT '>> B2. DocumentEntries...'
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DocumentEntries')
BEGIN
    CREATE TABLE DocumentEntries (
        Id              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ProductId       INT NOT NULL,
        FileName        NVARCHAR(500) NOT NULL,
        StoredFileName  NVARCHAR(500) NOT NULL,
        StoredPath      NVARCHAR(1000) NOT NULL,
        FileSize        BIGINT NOT NULL,
        ContentType     NVARCHAR(100) NOT NULL,
        UploadedById    NVARCHAR(450) NOT NULL,
        UploadedAt      DATETIME2 NOT NULL,
        IsLatest        BIT NOT NULL,
        Notes           NVARCHAR(500) NULL,
        CONSTRAINT FK_DocumentEntries_AspNetUsers_UploadedById 
            FOREIGN KEY (UploadedById) REFERENCES AspNetUsers(Id) ON DELETE CASCADE,
        CONSTRAINT FK_DocumentEntries_DocumentProducts_ProductId 
            FOREIGN KEY (ProductId) REFERENCES DocumentProducts(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_DocumentEntries_ProductId_IsLatest ON DocumentEntries(ProductId, IsLatest);
    CREATE INDEX IX_DocumentEntries_UploadedAt ON DocumentEntries(UploadedAt);
    CREATE INDEX IX_DocumentEntries_UploadedById ON DocumentEntries(UploadedById);
    PRINT '   + DocumentEntries creada'
END
ELSE PRINT '   = DocumentEntries ya existe'
GO

-- =====================================================================
-- B3. DocumentAuditLogs
-- =====================================================================
PRINT '>> B3. DocumentAuditLogs...'
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DocumentAuditLogs')
BEGIN
    CREATE TABLE DocumentAuditLogs (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EntryId     INT NULL,
        ProductId   INT NOT NULL,
        [Action]    NVARCHAR(50) NOT NULL,
        UserId      NVARCHAR(MAX) NOT NULL,
        UserName    NVARCHAR(200) NOT NULL,
        [Timestamp] DATETIME2 NOT NULL,
        Details     NVARCHAR(500) NULL,
        CONSTRAINT FK_DocumentAuditLogs_DocumentEntries_EntryId 
            FOREIGN KEY (EntryId) REFERENCES DocumentEntries(Id),
        CONSTRAINT FK_DocumentAuditLogs_DocumentProducts_ProductId 
            FOREIGN KEY (ProductId) REFERENCES DocumentProducts(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_DocumentAuditLogs_EntryId ON DocumentAuditLogs(EntryId);
    CREATE INDEX IX_DocumentAuditLogs_ProductId ON DocumentAuditLogs(ProductId);
    CREATE INDEX IX_DocumentAuditLogs_Timestamp ON DocumentAuditLogs([Timestamp]);
    PRINT '   + DocumentAuditLogs creada'
END
ELSE PRINT '   = DocumentAuditLogs ya existe'
GO

-- =====================================================================
-- B4. UserProductPermissions (PK compuesto)
-- =====================================================================
PRINT '>> B4. UserProductPermissions...'
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'UserProductPermissions')
BEGIN
    CREATE TABLE UserProductPermissions (
        UserId      NVARCHAR(450) NOT NULL,
        ProductId   INT NOT NULL,
        CONSTRAINT PK_UserProductPermissions PRIMARY KEY (UserId, ProductId),
        CONSTRAINT FK_UserProductPermissions_AspNetUsers_UserId 
            FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE,
        CONSTRAINT FK_UserProductPermissions_DocumentProducts_ProductId 
            FOREIGN KEY (ProductId) REFERENCES DocumentProducts(Id) ON DELETE CASCADE
    );
    PRINT '   + UserProductPermissions creada'
END
ELSE PRINT '   = UserProductPermissions ya existe'
GO

-- =============================================================================
-- PARTE C: MÓDULO DE USUARIOS - Nuevas columnas en AspNetUsers
-- =============================================================================

-- =====================================================================
-- C1. Campos de aprobación (Migración 6: AddUserApproval)
-- =====================================================================
PRINT '>> C1. Campos de aprobación en AspNetUsers...'

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AspNetUsers') AND name = 'IsApproved')
BEGIN
    ALTER TABLE AspNetUsers ADD IsApproved BIT NOT NULL DEFAULT 1;
    PRINT '   + IsApproved agregada'
END
ELSE PRINT '   = IsApproved ya existe'

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AspNetUsers') AND name = 'ApprovedBy')
BEGIN
    ALTER TABLE AspNetUsers ADD ApprovedBy NVARCHAR(MAX) NULL;
    PRINT '   + ApprovedBy agregada'
END
ELSE PRINT '   = ApprovedBy ya existe'

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AspNetUsers') AND name = 'ApprovedAt')
BEGIN
    ALTER TABLE AspNetUsers ADD ApprovedAt DATETIME2 NULL;
    PRINT '   + ApprovedAt agregada'
END
ELSE PRINT '   = ApprovedAt ya existe'

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AspNetUsers') AND name = 'RegistrationNote')
BEGIN
    ALTER TABLE AspNetUsers ADD RegistrationNote NVARCHAR(MAX) NULL;
    PRINT '   + RegistrationNote agregada'
END
ELSE PRINT '   = RegistrationNote ya existe'
GO

-- =====================================================================
-- C2. CentrosTrabajo + FK (Migración 7: AddOrganismoAndCentroTrabajo)
-- =====================================================================
PRINT '>> C2. CentrosTrabajo y campos Organismo/CT en AspNetUsers...'

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CentrosTrabajo')
BEGIN
    CREATE TABLE CentrosTrabajo (
        Id      INT IDENTITY(1,1) PRIMARY KEY,
        Nombre  NVARCHAR(MAX) NOT NULL,
        Activo  BIT NOT NULL DEFAULT 1
    );
    PRINT '   + Tabla CentrosTrabajo creada'

    INSERT INTO CentrosTrabajo (Nombre, Activo) VALUES (N'SUBGERENCIA HIDROGRIJALVA E HIDROMETRÍA', 1);
    PRINT '   + Seed: SUBGERENCIA HIDROGRIJALVA E HIDROMETRÍA'
END
ELSE PRINT '   = CentrosTrabajo ya existe'

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AspNetUsers') AND name = 'OrganismoId')
BEGIN
    ALTER TABLE AspNetUsers ADD OrganismoId INT NULL;
    PRINT '   + OrganismoId agregada'
END
ELSE PRINT '   = OrganismoId ya existe'

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AspNetUsers') AND name = 'CentroTrabajoId')
BEGIN
    ALTER TABLE AspNetUsers ADD CentroTrabajoId INT NULL;
    CREATE INDEX IX_AspNetUsers_CentroTrabajoId ON AspNetUsers(CentroTrabajoId);
    ALTER TABLE AspNetUsers ADD CONSTRAINT FK_AspNetUsers_CentrosTrabajo_CentroTrabajoId
        FOREIGN KEY (CentroTrabajoId) REFERENCES CentrosTrabajo(Id) ON DELETE SET NULL;
    PRINT '   + CentroTrabajoId + FK agregada'
END
ELSE PRINT '   = CentroTrabajoId ya existe'
GO

-- =====================================================================
-- C3. Trabajador CFE / Empresa externa (Migración 8: AddEsTrabajadorCFEFields)
-- =====================================================================
PRINT '>> C3. Campos EsTrabajadorCFE en AspNetUsers...'

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AspNetUsers') AND name = 'EsTrabajadorCFE')
BEGIN
    ALTER TABLE AspNetUsers ADD EsTrabajadorCFE BIT NOT NULL DEFAULT 1;
    PRINT '   + EsTrabajadorCFE agregada'
END
ELSE PRINT '   = EsTrabajadorCFE ya existe'

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AspNetUsers') AND name = 'EmpresaExterna')
BEGIN
    ALTER TABLE AspNetUsers ADD EmpresaExterna NVARCHAR(MAX) NULL;
    PRINT '   + EmpresaExterna agregada'
END
ELSE PRINT '   = EmpresaExterna ya existe'

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AspNetUsers') AND name = 'DepartamentoExterno')
BEGIN
    ALTER TABLE AspNetUsers ADD DepartamentoExterno NVARCHAR(MAX) NULL;
    PRINT '   + DepartamentoExterno agregada'
END
ELSE PRINT '   = DepartamentoExterno ya existe'
GO

-- =============================================================================
-- PARTE D: AUDITORÍA DE LOGIN (Migración 5: AddLoginAudit)
-- =============================================================================
PRINT '>> D. LoginAudits...'

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'LoginAudits')
BEGIN
    CREATE TABLE LoginAudits (
        Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
        UserId          NVARCHAR(450) NULL,
        UserName        NVARCHAR(450) NOT NULL,
        [Timestamp]     DATETIME2 NOT NULL,
        Success         BIT NOT NULL,
        FailureReason   NVARCHAR(MAX) NULL,
        IpAddress       NVARCHAR(MAX) NULL,
        UserAgent       NVARCHAR(MAX) NULL,
        Provider        NVARCHAR(MAX) NULL
    );
    CREATE INDEX IX_LoginAudits_Timestamp ON LoginAudits([Timestamp]);
    CREATE INDEX IX_LoginAudits_UserId ON LoginAudits(UserId);
    CREATE INDEX IX_LoginAudits_UserName ON LoginAudits(UserName);
    PRINT '   + Tabla LoginAudits creada'
END
ELSE PRINT '   = LoginAudits ya existe'
GO

-- =============================================================================
-- PARTE E: TABLAS OPERATIVAS (Dapper - no EF)
-- =============================================================================

-- =====================================================================
-- E1. FunVasosReferencias (Asíntotas configurables de gráficas de presas)
-- =====================================================================
PRINT '>> E1. FunVasosReferencias...'

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
    PRINT '   + Tabla FunVasosReferencias creada'
END
ELSE PRINT '   = FunVasosReferencias ya existe'
GO

-- =====================================================================
-- E2. Mantenimiento de Estaciones (3 tablas)
-- =====================================================================
PRINT '>> E2. Tablas de Mantenimiento...'

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MantenimientoOrden')
BEGIN
    CREATE TABLE MantenimientoOrden (
        Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
        IdEstacion      UNIQUEIDENTIFIER NOT NULL,
        TipoMantenimiento NVARCHAR(50) NOT NULL DEFAULT 'Correctivo',
        Descripcion     NVARCHAR(MAX) NULL,
        FechaInicio     DATETIME2 NOT NULL,
        FechaFin        DATETIME2 NULL,
        Estado          NVARCHAR(30) NOT NULL DEFAULT 'Programado',
        AislarDatos     BIT NOT NULL DEFAULT 1,
        Prioridad       NVARCHAR(20) NULL DEFAULT 'Normal',
        ResponsableNombre NVARCHAR(200) NULL,
        Observaciones   NVARCHAR(MAX) NULL,
        CreadoPor       NVARCHAR(450) NULL,
        CreadoPorNombre NVARCHAR(200) NULL,
        FechaCreacion   DATETIME2 NOT NULL DEFAULT GETDATE(),
        ModificadoPor   NVARCHAR(450) NULL,
        ModificadoPorNombre NVARCHAR(200) NULL,
        FechaModificacion DATETIME2 NULL,
        CONSTRAINT FK_MantenimientoOrden_Estacion 
            FOREIGN KEY (IdEstacion) REFERENCES Estacion(Id)
    );
    CREATE INDEX IX_MantenimientoOrden_Estado ON MantenimientoOrden(Estado) INCLUDE (IdEstacion, AislarDatos);
    CREATE INDEX IX_MantenimientoOrden_Estacion ON MantenimientoOrden(IdEstacion) INCLUDE (Estado, AislarDatos, FechaInicio, FechaFin);
    CREATE INDEX IX_MantenimientoOrden_Aislamiento ON MantenimientoOrden(AislarDatos, Estado) WHERE AislarDatos = 1 AND Estado IN ('En Proceso', 'Programado');
    PRINT '   + MantenimientoOrden creada'
END
ELSE PRINT '   = MantenimientoOrden ya existe'

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MantenimientoBitacora')
BEGIN
    CREATE TABLE MantenimientoBitacora (
        Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
        IdOrden         BIGINT NOT NULL,
        Descripcion     NVARCHAR(MAX) NULL,
        FechaEvento     DATETIME2 NOT NULL,
        FechaRegistro   DATETIME2 NOT NULL DEFAULT GETDATE(),
        Usuario         NVARCHAR(450) NULL,
        UsuarioNombre   NVARCHAR(200) NULL,
        CONSTRAINT FK_MantenimientoBitacora_Orden 
            FOREIGN KEY (IdOrden) REFERENCES MantenimientoOrden(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_MantenimientoBitacora_Orden ON MantenimientoBitacora(IdOrden) INCLUDE (FechaEvento);
    PRINT '   + MantenimientoBitacora creada'
END
ELSE PRINT '   = MantenimientoBitacora ya existe'

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MantenimientoAdjunto')
BEGIN
    CREATE TABLE MantenimientoAdjunto (
        Id               BIGINT IDENTITY(1,1) PRIMARY KEY,
        IdOrden          BIGINT NOT NULL,
        IdBitacora       BIGINT NULL,
        NombreOriginal   NVARCHAR(500) NOT NULL,
        NombreAlmacenado NVARCHAR(500) NOT NULL,
        RutaArchivo      NVARCHAR(1000) NOT NULL,
        TipoArchivo      NVARCHAR(200) NULL,
        TamanoBytes      BIGINT NOT NULL DEFAULT 0,
        SubidoPor        NVARCHAR(450) NULL,
        SubidoPorNombre  NVARCHAR(200) NULL,
        FechaSubido      DATETIME2 NOT NULL DEFAULT GETDATE(),
        CONSTRAINT FK_MantenimientoAdjunto_Orden 
            FOREIGN KEY (IdOrden) REFERENCES MantenimientoOrden(Id) ON DELETE CASCADE,
        CONSTRAINT FK_MantenimientoAdjunto_Bitacora 
            FOREIGN KEY (IdBitacora) REFERENCES MantenimientoBitacora(Id)
    );
    CREATE INDEX IX_MantenimientoAdjunto_Orden ON MantenimientoAdjunto(IdOrden) INCLUDE (IdBitacora);
    PRINT '   + MantenimientoAdjunto creada'
END
ELSE PRINT '   = MantenimientoAdjunto ya existe'
GO

-- =====================================================================
-- E3. AlertRecord (Early Warning / Alertas Tempranas)
-- =====================================================================
PRINT '>> E3. AlertRecord...'

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
    );
    CREATE INDEX IX_AlertRecord_Fecha ON AlertRecord(FechaAlerta DESC);
    CREATE INDEX IX_AlertRecord_Sensor ON AlertRecord(IdSensor);
    PRINT '   + AlertRecord creada'
END
ELSE PRINT '   = AlertRecord ya existe'
GO

-- =====================================================================
-- E4. ChatMessages (Chat SignalR)
-- =====================================================================
PRINT '>> E4. ChatMessages...'

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ChatMessages')
BEGIN
    CREATE TABLE ChatMessages (
        Id          UNIQUEIDENTIFIER PRIMARY KEY,
        ChatId      UNIQUEIDENTIFIER NOT NULL,
        Room        NVARCHAR(255) NOT NULL DEFAULT 'general',
        UserId      NVARCHAR(450) NOT NULL,
        UserName    NVARCHAR(256) NOT NULL,
        FullName    NVARCHAR(200),
        [Message]   NVARCHAR(MAX) NOT NULL,
        [Timestamp] DATETIME2 NOT NULL
    );
    CREATE INDEX IX_ChatMessages_Room ON ChatMessages(Room, [Timestamp] DESC);
    CREATE INDEX IX_ChatMessages_User ON ChatMessages(UserId);
    PRINT '   + ChatMessages creada'
END
ELSE PRINT '   = ChatMessages ya existe'
GO

-- =====================================================================
-- E5. DeviceTokens (Push Notifications FCM)
-- =====================================================================
PRINT '>> E5. DeviceTokens...'

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DeviceTokens')
BEGIN
    CREATE TABLE DeviceTokens (
        Id          BIGINT IDENTITY(1,1) PRIMARY KEY,
        UserId      NVARCHAR(450) NOT NULL,
        Token       NVARCHAR(500) NOT NULL,
        [Platform]  NVARCHAR(20) NOT NULL DEFAULT 'android',
        CreatedAt   DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        LastSeen    DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
    CREATE UNIQUE INDEX IX_DeviceTokens_UserToken ON DeviceTokens(UserId, Token);
    PRINT '   + DeviceTokens creada'
END
ELSE PRINT '   = DeviceTokens ya existe'
GO

-- =====================================================================
-- E6. Organismo (tabla legacy Dapper)
-- =====================================================================
PRINT '>> E6. Tabla Organismo...'

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Organismo')
BEGIN
    CREATE TABLE Organismo (
        Id          INT IDENTITY(1,1) PRIMARY KEY,
        Nombre      NVARCHAR(200) NOT NULL,
        Iniciales   NVARCHAR(20) NULL,
        Activo      BIT NOT NULL DEFAULT 1
    );
    INSERT INTO Organismo (Nombre, Iniciales, Activo) VALUES 
        (N'Organismo de Cuenca Grijalva y Nacajuca', 'OCGN', 1),
        (N'Subgerencia HidroGrijalva', 'SHG', 1);
    PRINT '   + Tabla Organismo creada con seeds'
END
ELSE PRINT '   = Organismo ya existe'
GO

-- =============================================================================
-- PARTE F: COLUMNAS ADICIONALES EN TABLAS LEGACY
-- =============================================================================

-- =====================================================================
-- F1. Cuenca/Subcuenca - KML y VerEnMapa
-- =====================================================================
PRINT '>> F1. Columnas KML y VerEnMapa en Cuenca/Subcuenca...'

IF OBJECT_ID('Cuenca', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Cuenca') AND name = 'Codigo')
        ALTER TABLE Cuenca ADD Codigo NVARCHAR(20) NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Cuenca') AND name = 'ArchivoKml')
        ALTER TABLE Cuenca ADD ArchivoKml NVARCHAR(255) NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Cuenca') AND name = 'Color')
        ALTER TABLE Cuenca ADD Color NVARCHAR(10) NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Cuenca') AND name = 'VerEnMapa')
        ALTER TABLE Cuenca ADD VerEnMapa BIT NOT NULL DEFAULT 0;
    PRINT '   Cuenca actualizada'
END
ELSE PRINT '   ! Tabla Cuenca no existe (legacy)'

IF OBJECT_ID('Subcuenca', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Subcuenca') AND name = 'ArchivoKml')
        ALTER TABLE Subcuenca ADD ArchivoKml NVARCHAR(255) NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Subcuenca') AND name = 'Color')
        ALTER TABLE Subcuenca ADD Color NVARCHAR(10) NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Subcuenca') AND name = 'VerEnMapa')
        ALTER TABLE Subcuenca ADD VerEnMapa BIT NOT NULL DEFAULT 0;
    PRINT '   Subcuenca actualizada'
END
ELSE PRINT '   ! Tabla Subcuenca no existe (legacy)'
GO

-- =============================================================================
-- PARTE G: SEED DATA (Roles, Admin, Productos)
-- =============================================================================

-- =====================================================================
-- G1. Roles del sistema
-- =====================================================================
PRINT '>> G1. Seed de Roles...'

-- SuperAdmin
IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = 'SUPERADMIN')
BEGIN
    INSERT INTO AspNetRoles (Id, [Name], NormalizedName, ConcurrencyStamp)
    VALUES (NEWID(), 'SuperAdmin', 'SUPERADMIN', NEWID());
    PRINT '   + Rol SuperAdmin creado'
END
ELSE PRINT '   = SuperAdmin ya existe'

-- Administrador
IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = 'ADMINISTRADOR')
BEGIN
    INSERT INTO AspNetRoles (Id, [Name], NormalizedName, ConcurrencyStamp)
    VALUES (NEWID(), 'Administrador', 'ADMINISTRADOR', NEWID());
    PRINT '   + Rol Administrador creado'
END
ELSE PRINT '   = Administrador ya existe'

-- Operador
IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = 'OPERADOR')
BEGIN
    INSERT INTO AspNetRoles (Id, [Name], NormalizedName, ConcurrencyStamp)
    VALUES (NEWID(), 'Operador', 'OPERADOR', NEWID());
    PRINT '   + Rol Operador creado'
END
ELSE PRINT '   = Operador ya existe'

-- Visualizador
IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = 'VISUALIZADOR')
BEGIN
    INSERT INTO AspNetRoles (Id, [Name], NormalizedName, ConcurrencyStamp)
    VALUES (NEWID(), 'Visualizador', 'VISUALIZADOR', NEWID());
    PRINT '   + Rol Visualizador creado'
END
ELSE PRINT '   = Visualizador ya existe'
GO

-- =====================================================================
-- G2. Usuario administrador por defecto
-- NOTA: El password hash se genera por la app al primer arranque.
--       Este INSERT crea el usuario SIN password. La app (SeedData.cs)
--       lo detecta y asigna la contraseña Cfe2026## si no la tiene.
--       Si prefieres hacerlo todo por SQL, descomenta el bloque con hash.
-- =====================================================================
PRINT '>> G2. Usuario administrador...'
PRINT '   (El usuario admin se crea al iniciar la aplicación via SeedData.cs)'
PRINT '   (Usuario: administrador / Password: Cfe2026## / Rol: SuperAdmin)'
GO

-- =====================================================================
-- G3. Productos de documentos
-- =====================================================================
PRINT '>> G3. Seed de DocumentProducts...'

IF NOT EXISTS (SELECT 1 FROM DocumentProducts WHERE Code = 'boletin')
BEGIN
    INSERT INTO DocumentProducts ([Name], Code, [Description], FilePrefix, StoragePath, IsActive, RequiredDaily, CreatedAt)
    VALUES (N'Boletín Hidrometeorológico de Generación', 'boletin', 
            N'Reporte diario hidrometeorológico de generación', 'BHG', '', 1, 1, GETUTCDATE());
    PRINT '   + Producto boletin creado'
END
ELSE PRINT '   = boletin ya existe'

IF NOT EXISTS (SELECT 1 FROM DocumentProducts WHERE Code = 'vasos')
BEGIN
    INSERT INTO DocumentProducts ([Name], Code, [Description], FilePrefix, StoragePath, IsActive, RequiredDaily, CreatedAt)
    VALUES (N'Funcionamiento de Vasos', 'vasos', 
            N'Reporte diario de niveles, almacenamiento y operación de vasos/presas', 'FIN', '', 1, 1, GETUTCDATE());
    PRINT '   + Producto vasos creado'
END
ELSE PRINT '   = vasos ya existe'

IF NOT EXISTS (SELECT 1 FROM DocumentProducts WHERE Code = 'red_telemetrica')
BEGIN
    INSERT INTO DocumentProducts ([Name], Code, [Description], FilePrefix, StoragePath, IsActive, RequiredDaily, CreatedAt)
    VALUES (N'Red Telemétrica', 'red_telemetrica', 
            N'Reporte diario de la red telemétrica de estaciones', 'NRED', '', 1, 1, GETUTCDATE());
    PRINT '   + Producto red_telemetrica creado'
END
ELSE PRINT '   = red_telemetrica ya existe'
GO

-- =============================================================================
-- PARTE H: REGISTRO DE MIGRACIONES EF CORE
-- =============================================================================
PRINT '>> H. Registrando migraciones en __EFMigrationsHistory...'

IF NOT EXISTS (SELECT * FROM __EFMigrationsHistory WHERE MigrationId = '20260216173254_InitialIdentity')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260216173254_InitialIdentity', '8.0.2');

IF NOT EXISTS (SELECT * FROM __EFMigrationsHistory WHERE MigrationId = '20260216180137_AddDocumentRepository')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260216180137_AddDocumentRepository', '8.0.2');

IF NOT EXISTS (SELECT * FROM __EFMigrationsHistory WHERE MigrationId = '20260216181449_AddDocumentStoragePath')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260216181449_AddDocumentStoragePath', '8.0.2');

IF NOT EXISTS (SELECT * FROM __EFMigrationsHistory WHERE MigrationId = '20260216184224_AddDocumentFilePrefix')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260216184224_AddDocumentFilePrefix', '8.0.2');

IF NOT EXISTS (SELECT * FROM __EFMigrationsHistory WHERE MigrationId = '20260331021529_AddLoginAudit')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260331021529_AddLoginAudit', '8.0.2');

IF NOT EXISTS (SELECT * FROM __EFMigrationsHistory WHERE MigrationId = '20260331022704_AddUserApproval')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260331022704_AddUserApproval', '8.0.2');

IF NOT EXISTS (SELECT * FROM __EFMigrationsHistory WHERE MigrationId = '20260331031134_AddOrganismoAndCentroTrabajo')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260331031134_AddOrganismoAndCentroTrabajo', '8.0.2');

IF NOT EXISTS (SELECT * FROM __EFMigrationsHistory WHERE MigrationId = '20260331141542_AddEsTrabajadorCFEFields')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260331141542_AddEsTrabajadorCFEFields', '8.0.2');

PRINT '   8 migraciones registradas'
GO

-- =============================================================================
-- PARTE I: ACTUALIZACIONES FINALES
-- =============================================================================
PRINT '>> I. Actualizaciones finales...'

-- Marcar usuarios existentes como aprobados y trabajadores CFE
UPDATE AspNetUsers SET IsApproved = 1 WHERE IsApproved = 0 OR IsApproved IS NULL;
UPDATE AspNetUsers SET EsTrabajadorCFE = 1 WHERE EsTrabajadorCFE = 0 AND EmpresaExterna IS NULL;
PRINT '   Usuarios existentes actualizados (IsApproved=1, EsTrabajadorCFE=1)'
GO

-- =============================================================================
-- VERIFICACIÓN FINAL
-- =============================================================================
PRINT ''
PRINT '================================================================'
PRINT ' VERIFICACIÓN DE TABLAS'
PRINT '================================================================'

SELECT 'Identity' AS Grupo, t.name AS Tabla, 
       (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS Columnas
FROM sys.tables t 
WHERE t.name IN ('AspNetUsers','AspNetRoles','AspNetRoleClaims','AspNetUserClaims',
                 'AspNetUserLogins','AspNetUserRoles','AspNetUserTokens')
UNION ALL
SELECT 'Documentos', t.name, 
       (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id)
FROM sys.tables t 
WHERE t.name IN ('DocumentProducts','DocumentEntries','DocumentAuditLogs','UserProductPermissions')
UNION ALL
SELECT 'Operativas', t.name, 
       (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id)
FROM sys.tables t 
WHERE t.name IN ('LoginAudits','CentrosTrabajo','Organismo','FunVasosReferencias',
                 'MantenimientoOrden','MantenimientoBitacora','MantenimientoAdjunto',
                 'AlertRecord','ChatMessages','DeviceTokens')
ORDER BY 1, 2;

SELECT 'Roles' AS Tipo, [Name] AS Valor FROM AspNetRoles ORDER BY [Name];
SELECT 'Productos' AS Tipo, Code + ' (' + [Name] + ')' AS Valor FROM DocumentProducts ORDER BY Code;
SELECT 'Migraciones' AS Tipo, MigrationId AS Valor FROM __EFMigrationsHistory ORDER BY MigrationId;

PRINT ''
PRINT '================================================================'
PRINT ' DEPLOY COMPLETADO EXITOSAMENTE'
PRINT ' Fecha: ' + CONVERT(VARCHAR, GETDATE(), 120)
PRINT '================================================================'
GO
