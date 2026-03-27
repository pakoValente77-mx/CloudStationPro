@echo off
title Instalador - Pronostico de Lluvia CloudStation
echo ============================================================
echo   INSTALADOR DE PRONOSTICO DE LLUVIA - CloudStation
echo   Configura Python, dependencias y tarea programada
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
set INSTALL_DIR=C:\IGSCLOUD\RainForecast
set PYTHON_EXE=python
set LOG_DIR=%INSTALL_DIR%\logs
set TASK_NAME=CloudStation_SyncRainForecast

echo [1/5] Verificando Python...
%PYTHON_EXE% --version >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] Python no encontrado. Instale Python 3.10+ y agregue al PATH.
    pause
    exit /b 1
)
for /f "tokens=2 delims= " %%v in ('%PYTHON_EXE% --version 2^>^&1') do echo        Python %%v encontrado

echo.
echo [2/5] Creando directorios...
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"
echo        %INSTALL_DIR% OK
echo        %LOG_DIR% OK

echo.
echo [3/5] Copiando archivos...
copy /Y "%~dp0sync_rain_forecast.py" "%INSTALL_DIR%\" >nul
copy /Y "%~dp0setup_rain_forecast.py" "%INSTALL_DIR%\" >nul
copy /Y "%~dp0config.ini" "%INSTALL_DIR%\" >nul
copy /Y "%~dp0requirements.txt" "%INSTALL_DIR%\" >nul
copy /Y "%~dp0setup_inicial_pronostico.bat" "%INSTALL_DIR%\" >nul

REM Copiar KMLs necesarios para setup
set KML_SRC=%~dp0CloudStationWeb\KML Cuencas y Rios Grijalva
set KML_DST=%INSTALL_DIR%\CloudStationWeb\KML Cuencas y Rios Grijalva
if not exist "%KML_DST%" mkdir "%KML_DST%"
if exist "%KML_SRC%" (
    copy /Y "%KML_SRC%\*.kml" "%KML_DST%\" >nul 2>&1
    echo        KMLs de subcuencas copiados
) else (
    echo [WARN] No se encontraron KMLs en %KML_SRC%
    echo        Copie manualmente los archivos Subcuencas_*.kml a:
    echo        %KML_DST%
)
echo        Archivos copiados a %INSTALL_DIR%

echo.
echo [4/5] Instalando dependencias Python...
%PYTHON_EXE% -m pip install --upgrade pip >nul 2>&1
%PYTHON_EXE% -m pip install psycopg2-binary shapely schedule >nul 2>&1
if %errorLevel% neq 0 (
    echo [WARN] Error al instalar paquetes. Intentando con requirements.txt...
    %PYTHON_EXE% -m pip install -r "%INSTALL_DIR%\requirements.txt" 2>&1
)
echo        Dependencias instaladas

echo.
echo [5/5] Creando tarea programada en Windows...

REM Eliminar tarea anterior si existe
schtasks /delete /tn "%TASK_NAME%" /f >nul 2>&1

REM Crear tarea que ejecuta el daemon cada vez que el sistema arranca
schtasks /create ^
    /tn "%TASK_NAME%" ^
    /tr "\"%PYTHON_EXE%\" \"%INSTALL_DIR%\sync_rain_forecast.py\" --daemon" ^
    /sc ONSTART ^
    /ru SYSTEM ^
    /rl HIGHEST ^
    /f

if %errorLevel% equ 0 (
    echo        Tarea "%TASK_NAME%" creada exitosamente
) else (
    echo [WARN] No se pudo crear la tarea. Se creara un acceso directo alternativo.
)

REM -- Crear script de inicio rapido --
echo @echo off > "%INSTALL_DIR%\iniciar_sync.bat"
echo title CloudStation - Sync Rain Forecast >> "%INSTALL_DIR%\iniciar_sync.bat"
echo cd /d "%INSTALL_DIR%" >> "%INSTALL_DIR%\iniciar_sync.bat"
echo echo Iniciando sincronizacion de pronostico... >> "%INSTALL_DIR%\iniciar_sync.bat"
echo %PYTHON_EXE% sync_rain_forecast.py --daemon >> "%INSTALL_DIR%\iniciar_sync.bat"
echo pause >> "%INSTALL_DIR%\iniciar_sync.bat"
echo        Script de inicio: %INSTALL_DIR%\iniciar_sync.bat

echo.
echo ============================================================
echo   INSTALACION COMPLETA
echo ============================================================
echo.
echo   Archivos en:  %INSTALL_DIR%
echo   Logs en:      %LOG_DIR%
echo   Tarea:        %TASK_NAME% (se ejecuta al iniciar Windows)
echo.
echo   Para ejecutar manualmente:
echo     %INSTALL_DIR%\iniciar_sync.bat
echo.
echo   Para ejecutar el setup inicial (solo primera vez):
echo     cd %INSTALL_DIR%
echo     python setup_rain_forecast.py
echo.
echo   El daemon se ejecuta cada hora al minuto :40
echo   y descarga automaticamente el pronostico del dia.
echo ============================================================
pause
