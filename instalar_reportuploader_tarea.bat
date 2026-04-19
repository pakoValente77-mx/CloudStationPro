@echo off
REM ============================================================
REM instalar_reportuploader_tarea.bat
REM Instala ReportUploader como Tarea Programada de Windows
REM Se ejecuta cada 1 minuto para subir reportes al servidor PIH
REM ============================================================
echo.
echo ============================================
echo   Instalador: ReportUploader (Tarea Prog.)
echo ============================================
echo.

REM --- Configuración ---
set TASK_NAME=PIH_ReportUploader
set SERVER_URL=http://atlas16.ddns.net:5215
set API_KEY=***REDACTED-API-KEY***
set WATCH_DIR=C:\PIH\reportes
set INSTALL_DIR=C:\PIH\ReportUploader
set INTERVAL=60

echo Servidor  : %SERVER_URL%
echo Watch Dir : %WATCH_DIR%
echo Install   : %INSTALL_DIR%
echo Intervalo : %INTERVAL%s
echo.

REM --- Crear directorios ---
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
if not exist "%WATCH_DIR%" mkdir "%WATCH_DIR%"

REM --- Publicar ReportUploader ---
echo [1/3] Compilando ReportUploader...
cd /d "%~dp0.."
dotnet publish ReportUploader\ReportUploader.csproj -c Release -o "%INSTALL_DIR%" --nologo
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Falló el publish de ReportUploader.
    pause
    exit /b 1
)

REM --- Crear script de ejecución ---
echo [2/3] Creando script de ejecución...
(
echo @echo off
echo cd /d "%INSTALL_DIR%"
echo "%INSTALL_DIR%\ReportUploader.exe" --server %SERVER_URL% --key %API_KEY% --watch "%WATCH_DIR%" --interval %INTERVAL%
) > "%INSTALL_DIR%\run_reportuploader.bat"

REM --- Eliminar tarea anterior si existe ---
schtasks /query /tn "%TASK_NAME%" >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo    Eliminando tarea anterior...
    schtasks /delete /tn "%TASK_NAME%" /f >nul 2>&1
)

REM --- Crear tarea programada (al inicio del sistema) ---
echo [3/3] Registrando tarea programada...
schtasks /create /tn "%TASK_NAME%" ^
    /tr "\"%INSTALL_DIR%\run_reportuploader.bat\"" ^
    /sc ONSTART ^
    /ru SYSTEM ^
    /rl HIGHEST ^
    /f

if %ERRORLEVEL% NEQ 0 (
    echo ERROR: No se pudo crear la tarea programada.
    echo Asegúrese de ejecutar este script como Administrador.
    pause
    exit /b 1
)

REM --- Iniciar la tarea ahora ---
echo.
echo Iniciando ReportUploader...
schtasks /run /tn "%TASK_NAME%"

echo.
echo ============================================
echo   Instalación completada
echo ============================================
echo.
echo La tarea "%TASK_NAME%" se ejecutará:
echo   - Al inicio de Windows (automáticamente)
echo   - Cada %INTERVAL% segundos buscará cambios en:
echo     %WATCH_DIR%
echo.
echo Estructura esperada en %WATCH_DIR%:
echo   1.png   → Reporte de Unidades   (/1)
echo   2.png   → Power Monitoring       (/2)
echo   3.png   → Gráfica de Potencia    (/3)
echo   4.png   → Condición de Embalses  (/4)
echo   5.png   → Aportaciones Cuenca    (/5)
echo   reporte_lluvia_1_1_*.png → Lluvia 24h (/6)
echo   reporte_lluvia_1_2_*.png → Lluvia parcial (/7)
echo.
echo Para detener: schtasks /end /tn "%TASK_NAME%"
echo Para eliminar: schtasks /delete /tn "%TASK_NAME%" /f
echo.
pause
