@echo off
title Instalador - Modulo de Mantenimiento CloudStation
echo ============================================================
echo   INSTALADOR DEL MODULO DE MANTENIMIENTO - CloudStation
echo   Crea tablas en SQL Server (IGSCLOUD)
echo ============================================================
echo.

REM -- Verificar ejecucion como administrador --
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] Este script requiere ejecutarse como Administrador.
    echo         Click derecho -^> Ejecutar como administrador
    pause
    exit /b 1
)

REM -- Configuracion --
set SQL_SERVER=atlas16.ddns.net
set SQL_DATABASE=IGSCLOUD
set SQL_USER=sa
set SQL_PASS=Atlas2025$$
set ADJUNTOS_DIR=C:\IGSCLOUD\DocumentRepository\mantenimiento

echo [1/4] Verificando conectividad con SQL Server...
powershell -Command "Test-NetConnection %SQL_SERVER% -Port 1433 -WarningAction SilentlyContinue" | findstr "TcpTestSucceeded" | findstr "True" >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] No se puede conectar a %SQL_SERVER%:1433
    echo         Verifique la red y permisos de firewall.
    pause
    exit /b 1
)
echo        Conexion a %SQL_SERVER%:1433 OK

echo.
echo [2/4] Creando carpeta de adjuntos...
if not exist "%ADJUNTOS_DIR%" mkdir "%ADJUNTOS_DIR%"
echo        %ADJUNTOS_DIR% OK

echo.
echo [3/4] Ejecutando script SQL de mantenimiento...
echo        Creando tablas: MantenimientoOrden, MantenimientoBitacora, MantenimientoAdjunto
echo.

REM -- Buscar sqlcmd --
where sqlcmd >nul 2>&1
if %errorLevel% equ 0 (
    echo        Usando sqlcmd...
    sqlcmd -S %SQL_SERVER% -U %SQL_USER% -P %SQL_PASS% -d %SQL_DATABASE% -i "%~dp0CloudStationWeb\Migrations\add_maintenance_tables.sql"
    if %errorLevel% equ 0 (
        echo.
        echo        Tablas creadas exitosamente.
    ) else (
        echo.
        echo [ERROR] Error al ejecutar el script SQL.
        echo         Verifique las credenciales y que la BD exista.
        pause
        exit /b 1
    )
    goto :PASO4
)

REM -- Si no hay sqlcmd, intentar con PowerShell + Invoke-Sqlcmd --
echo        sqlcmd no encontrado, intentando con PowerShell...
powershell -Command "Import-Module SqlServer -ErrorAction SilentlyContinue" >nul 2>&1

REM -- Fallback: crear un script temporal y ejecutar con sqlcmd de SQL Server --
echo.
echo [AVISO] sqlcmd no esta instalado en este equipo.
echo.
echo Opciones para ejecutar el SQL manualmente:
echo.
echo   OPCION 1 - Instalar sqlcmd:
echo     winget install Microsoft.SqlServer.SqlCmd
echo     Luego vuelva a ejecutar este script.
echo.
echo   OPCION 2 - Desde SQL Server Management Studio (SSMS):
echo     1. Abra SSMS y conecte a: %SQL_SERVER%
echo     2. Seleccione la base de datos: %SQL_DATABASE%
echo     3. Abra el archivo:
echo        %~dp0CloudStationWeb\Migrations\add_maintenance_tables.sql
echo     4. Ejecute (F5)
echo.
echo   OPCION 3 - Desde PowerShell:
echo     Invoke-Sqlcmd -ServerInstance "%SQL_SERVER%" -Database "%SQL_DATABASE%" ^
echo       -Username "%SQL_USER%" -Password "%SQL_PASS%" ^
echo       -InputFile "%~dp0CloudStationWeb\Migrations\add_maintenance_tables.sql"
echo.
echo Presione cualquier tecla despues de ejecutar el SQL...
pause >nul

:PASO4
echo.
echo [4/4] Verificando permisos de carpeta IIS...

REM -- Dar permisos de escritura al App Pool de IIS sobre la carpeta de adjuntos --
icacls "%ADJUNTOS_DIR%" /grant "IIS_IUSRS:(OI)(CI)M" >nul 2>&1
icacls "%ADJUNTOS_DIR%" /grant "IUSR:(OI)(CI)M" >nul 2>&1
echo        Permisos de escritura en %ADJUNTOS_DIR% OK

echo.
echo ============================================================
echo   INSTALACION DEL MODULO DE MANTENIMIENTO COMPLETA
echo ============================================================
echo.
echo   Tablas creadas en: %SQL_SERVER% / %SQL_DATABASE%
echo     - MantenimientoOrden    (ordenes de trabajo)
echo     - MantenimientoBitacora (registro de actividades)
echo     - MantenimientoAdjunto  (fotos, documentos)
echo.
echo   Carpeta de adjuntos: %ADJUNTOS_DIR%
echo.
echo   El modulo esta disponible en el menu:
echo     Administracion -^> Mantenimiento de Estaciones
echo.
echo   Funcionalidades:
echo     - Crear ordenes de mantenimiento (preventivo/correctivo)
echo     - Bitacora de actividades por orden
echo     - Adjuntar fotos, oficios y documentos
echo     - Aislamiento automatico de datos durante mantenimiento
echo     - Filtrado por estacion, estado y fechas
echo ============================================================
pause
