@echo off
title CloudStation PIH - Build Produccion
setlocal enabledelayedexpansion

REM =====================================================================
REM  CLOUDSTATION PIH - Script de Publicacion para Windows
REM  Equivalente a publish_produccion.sh
REM  Requisitos: .NET 8 SDK instalado en la maquina de build
REM  Ejecucion: Desde la raiz del repositorio CloudStation
REM =====================================================================

echo ==============================================================
echo   CLOUDSTATION PIH - BUILD PRODUCCION (Windows)
echo   %DATE% %TIME%
echo ==============================================================
echo.

REM -- Verificar .NET SDK --
dotnet --version >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] .NET SDK no encontrado.
    echo         Instale .NET 8 SDK desde https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)
for /f "tokens=*" %%v in ('dotnet --version 2^>^&1') do echo        .NET SDK %%v encontrado

set PROJ_DIR=%~dp0CloudStationWeb
set PUBLISH_DIR=%~dp0CloudStationWeb\publish_produccion

echo.
echo [1/5] Limpiando build anterior...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
dotnet clean "%PROJ_DIR%\CloudStationWeb.csproj" -c Release --nologo -q
echo        Limpieza OK

echo.
echo [2/5] Compilando en modo Release...
dotnet build "%PROJ_DIR%\CloudStationWeb.csproj" -c Release --nologo -q
if %errorLevel% neq 0 (
    echo [ERROR] La compilacion fallo. Revise los errores arriba.
    pause
    exit /b 1
)
echo        Build OK (0 errores)

echo.
echo [3/5] Publicando para win-x64 (self-contained)...
dotnet publish "%PROJ_DIR%\CloudStationWeb.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -o "%PUBLISH_DIR%" ^
    --nologo -q
if %errorLevel% neq 0 (
    echo [ERROR] La publicacion fallo.
    pause
    exit /b 1
)
echo        Publicado en: %PUBLISH_DIR%

echo.
echo [4/5] Copiando archivos adicionales...

REM appsettings
copy /Y "%PROJ_DIR%\appsettings.json" "%PUBLISH_DIR%\" >nul
if exist "%PROJ_DIR%\appsettings.Development.json" (
    copy /Y "%PROJ_DIR%\appsettings.Development.json" "%PUBLISH_DIR%\" >nul
)

REM web.config para IIS
if exist "%PROJ_DIR%\web.config" (
    copy /Y "%PROJ_DIR%\web.config" "%PUBLISH_DIR%\" >nul
    echo        web.config copiado
)

REM KMLs de cuencas
set KML_SRC=%PROJ_DIR%\KML Cuencas y Rios Grijalva
set KML_DST=%PUBLISH_DIR%\KML Cuencas y Rios Grijalva
if exist "%KML_SRC%" (
    if not exist "%KML_DST%" mkdir "%KML_DST%"
    xcopy /Y /Q "%KML_SRC%\*" "%KML_DST%\" >nul
    echo        KMLs de cuencas copiados
)

REM Scripts SQL de referencia
copy /Y "%PROJ_DIR%\deploy_produccion_v2.sql" "%PUBLISH_DIR%\" >nul 2>&1
copy /Y "%PROJ_DIR%\deploy_timescale_v2.sql" "%PUBLISH_DIR%\" >nul 2>&1
echo        Archivos adicionales copiados

echo.
echo [5/5] Creando paquete ZIP para transferencia...
set ZIP_FILE=%~dp0CloudStationWeb\publish_produccion.zip
if exist "%ZIP_FILE%" del /q "%ZIP_FILE%"

powershell -NoProfile -Command ^
    "Compress-Archive -Path '%PUBLISH_DIR%\*' -DestinationPath '%ZIP_FILE%' -Force"
if %errorLevel% equ 0 (
    echo        ZIP creado: publish_produccion.zip
) else (
    echo [WARN] No se pudo crear el ZIP. Transfiera la carpeta manualmente.
)

echo.
echo ==============================================================
echo   BUILD COMPLETADO
echo ==============================================================
echo.
echo   Carpeta lista: %PUBLISH_DIR%
echo   ZIP listo:     %ZIP_FILE%
echo.
echo   SIGUIENTE PASO - En el servidor Windows:
echo     1. Copiar publish_produccion.zip a C:\inetpub\CloudStation\
echo     2. Ejecutar instalar_iis_cloudstation.bat como Administrador
echo        (configura IIS, permisos y carpetas automaticamente)
echo.
echo   SCRIPTS DE BASE DE DATOS (ejecutar una sola vez en servidor nuevo):
echo     SQL Server: deploy_produccion_v2.sql  -> contra IGSCLOUD
echo     TimescaleDB: deploy_timescale_v2.sql  -> contra mycloud_timescale
echo.
echo   SCRIPTS PYTHON:
echo     instalar_pronostico_windows.bat  -> daemon pronostico lluvia
echo     instalar_mantenimiento_windows.bat -> modulo mantenimiento
echo     instalar_watchdog_tarea.bat      -> daemon GOES/MyCloud
echo ==============================================================
echo.
pause
