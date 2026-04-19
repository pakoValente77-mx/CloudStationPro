@echo off
REM ══════════════════════════════════════════════════════════════
REM  Instalar BhgWatcher como Servicio Windows
REM  Ejecutar como Administrador
REM ══════════════════════════════════════════════════════════════

set SERVICE_NAME=BhgWatcher
set DISPLAY_NAME=BHG Watcher - CloudStation Pro
set INSTALL_DIR=C:\IGSCLOUD\BhgWatcher
set PROJECT_DIR=%~dp0

echo.
echo ╔══════════════════════════════════════════════════════════════╗
echo ║          Instalador de BhgWatcher como Servicio             ║
echo ╚══════════════════════════════════════════════════════════════╝
echo.

REM Verificar permisos de administrador
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Este script requiere permisos de Administrador.
    echo         Click derecho ^> Ejecutar como Administrador
    pause
    exit /b 1
)

REM Detener servicio si existe
echo [1/5] Deteniendo servicio existente...
sc.exe stop %SERVICE_NAME% >nul 2>&1
timeout /t 3 /nobreak >nul
sc.exe delete %SERVICE_NAME% >nul 2>&1
timeout /t 2 /nobreak >nul

REM Compilar
echo [2/5] Compilando BhgWatcher...
cd /d "%PROJECT_DIR%"
dotnet publish -c Release -o "%INSTALL_DIR%" --self-contained false
if %errorlevel% neq 0 (
    echo [ERROR] Falló la compilación.
    pause
    exit /b 1
)

REM Crear servicio
echo [3/5] Creando servicio Windows...
sc.exe create %SERVICE_NAME% binPath= "\"%INSTALL_DIR%\BhgWatcher.exe\"" start= auto DisplayName= "%DISPLAY_NAME%"
if %errorlevel% neq 0 (
    echo [ERROR] No se pudo crear el servicio.
    pause
    exit /b 1
)

REM Configurar descripción y recuperación
sc.exe description %SERVICE_NAME% "Monitorea la carpeta BHG en OneDrive y copia archivos nuevos a los inbox configurados en appsettings.json"
sc.exe failure %SERVICE_NAME% reset= 86400 actions= restart/5000/restart/10000/restart/30000

REM Iniciar servicio
echo [4/5] Iniciando servicio...
sc.exe start %SERVICE_NAME%
if %errorlevel% neq 0 (
    echo [WARN] No se pudo iniciar el servicio. Verifique appsettings.json
) else (
    echo [OK] Servicio iniciado correctamente.
)

echo.
echo [5/5] Instalación completada.
echo.
echo ────────────────────────────────────────────────────────────
echo  Servicio:       %SERVICE_NAME%
echo  Instalado en:   %INSTALL_DIR%
echo  Config:         %INSTALL_DIR%\appsettings.json
echo ────────────────────────────────────────────────────────────
echo.
echo  Comandos útiles:
echo    sc.exe stop BhgWatcher       (detener)
echo    sc.exe start BhgWatcher      (iniciar)
echo    sc.exe query BhgWatcher      (estado)
echo.
echo  Para agregar destinos, edite appsettings.json:
echo    %INSTALL_DIR%\appsettings.json
echo.
pause
