-- Tabla para asíntotas/referencias configurables por el usuario en gráficas FunVasos
-- Ejecutar en la base de datos IGSCLOUD (SQL Server)

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FunVasosReferencias')
BEGIN
    CREATE TABLE FunVasosReferencias (
        Id          BIGINT IDENTITY(1,1) PRIMARY KEY,
        PresaKey    NVARCHAR(100) NOT NULL,    -- Ej: 'Angostura', 'Chicoasen', 'Malpaso', etc.
        Nombre      NVARCHAR(200) NOT NULL,    -- Nombre de la referencia (ej: 'Nivel Crítico')
        Valor       DECIMAL(18,4) NOT NULL,    -- Valor numérico de la asíntota (m.s.n.m)
        Color       NVARCHAR(20) DEFAULT '#ffff00',  -- Color hex de la línea
        Visible     BIT DEFAULT 1,             -- Si se muestra o no
        UsuarioModifica NVARCHAR(256),
        FechaCreacion   DATETIME2 DEFAULT GETDATE(),
        FechaModifica   DATETIME2 DEFAULT GETDATE()
    );

    CREATE INDEX IX_FunVasosReferencias_PresaKey ON FunVasosReferencias(PresaKey);
END
GO
