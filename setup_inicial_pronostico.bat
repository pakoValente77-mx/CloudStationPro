@echo off
chcp 65001 >nul
title Setup Inicial - Pronóstico de Lluvia
echo ============================================================
echo   SETUP INICIAL - Esquema TimescaleDB + Polígonos KML
echo   Este script solo se ejecuta UNA VEZ
echo ============================================================
echo.

set INSTALL_DIR=%~dp0
cd /d "%INSTALL_DIR%"

echo Verificando Python...
python --version >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] Python no encontrado en PATH.
    pause
    exit /b 1
)

echo.
echo [1/2] Creando esquema en TimescaleDB...
echo        (rain_forecast.forecast, rain_forecast.rain_record, etc.)
echo.
python setup_rain_forecast.py
if %errorLevel% neq 0 (
    echo [ERROR] Fallo el setup. Verifique la conexion a TimescaleDB en config.ini
    pause
    exit /b 1
)

echo.
echo [2/2] Ejecutando primera sincronizacion de pronostico...
python sync_rain_forecast.py
if %errorLevel% neq 0 (
    echo [WARN] La primera sincronizacion tuvo problemas. Puede reintentarse.
)

echo.
echo ============================================================
echo   SETUP COMPLETO
echo ============================================================
echo.
echo   La base de datos esta lista.
echo   Ahora puede iniciar el daemon con:
echo     python sync_rain_forecast.py --daemon
echo.
echo   O ejecutar: iniciar_sync.bat
echo ============================================================
pause
