@echo off
title Instalador IIS - CloudStation PIH
setlocal enabledelayedexpansion

REM =====================================================================
REM  CLOUDSTATION PIH - Instalador IIS en Windows Server
REM  Requisitos:
REM    - Ejecutar como Administrador
REM    - .NET 8 Hosting Bundle instalado (no SDK, solo Runtime/Hosting)
REM    - IIS habilitado con modulo ASP.NET Core (AspNetCoreModuleV2)
REM    - publish_produccion.zip en la misma carpeta que este script
REM =====================================================================

REM -- Verificar ejecucion como administrador --
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] Este script requiere ejecutarse como Administrador.
    echo         Click derecho -^> Ejecutar como administrador
    pause
    exit /b 1
)

echo ==============================================================
echo   CLOUDSTATION PIH - INSTALADOR IIS
echo   %DATE% %TIME%
echo ==============================================================
echo.

REM ---- Configuracion (ajustar si el entorno es diferente) ----
set SITE_NAME=CloudStation
set APP_POOL=CloudStation
set SITE_PATH=C:\inetpub\CloudStation
set HTTP_PORT=80
set DOC_REPO=C:\IGSCLOUD\DocumentRepository
set LOG_DIR=%SITE_PATH%\logs
set KML_WWW=%SITE_PATH%\wwwroot\kml

REM Buscar el ZIP en la misma carpeta que este script
set ZIP_SRC=%~dp0publish_produccion.zip

echo   Ruta del sitio : %SITE_PATH%
echo   Puerto HTTP    : %HTTP_PORT%
echo   App Pool       : %APP_POOL%
echo.

REM =====================================================================
REM  PASO 1 — Verificar prerequisitos
REM =====================================================================
echo [1/7] Verificando prerequisitos...

REM Verificar IIS
sc query W3SVC >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] IIS (W3SVC) no esta instalado o no se detecta.
    echo         Instale IIS: Administrador del servidor -^> Agregar roles y caracteristicas
    echo         Roles necesarios: Web Server (IIS), WebSockets, Static Content
    pause
    exit /b 1
)
echo        IIS detectado OK

REM Verificar que existe el ZIP o la carpeta publish
if not exist "%ZIP_SRC%" (
    echo [WARN] No se encontro publish_produccion.zip junto a este script.
    echo        Asegurese de copiar publish_produccion.zip al mismo directorio.
    echo.
    set /p CONTINUAR=Continuar de todas formas? (S/N): 
    if /i "!CONTINUAR!" neq "S" exit /b 1
)

REM Verificar .NET Hosting Bundle (buscamos el modulo ASP.NET Core en IIS)
reg query "HKLM\SOFTWARE\Microsoft\IIS Extensions\IIS ASP.NET Core Module V2" >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] ASP.NET Core Module V2 no instalado en IIS.
    echo         Descargue e instale el .NET 8 Hosting Bundle:
    echo         https://dotnet.microsoft.com/download/dotnet/8.0
    echo         (seccion "ASP.NET Core Runtime" -^> Hosting Bundle)
    echo         Luego ejecute: iisreset
    pause
    exit /b 1
)
echo        ASP.NET Core Module V2 OK

echo.

REM =====================================================================
REM  PASO 2 — Crear carpetas
REM =====================================================================
echo [2/7] Creando estructura de carpetas...
if not exist "%SITE_PATH%"              mkdir "%SITE_PATH%"
if not exist "%LOG_DIR%"                mkdir "%LOG_DIR%"
if not exist "%KML_WWW%"                mkdir "%KML_WWW%"
if not exist "%DOC_REPO%\boletin"       mkdir "%DOC_REPO%\boletin"
if not exist "%DOC_REPO%\funvasos"      mkdir "%DOC_REPO%\funvasos"
if not exist "%DOC_REPO%\mantenimiento" mkdir "%DOC_REPO%\mantenimiento"
if not exist "C:\IGSCLOUD\Datos\GOES"   mkdir "C:\IGSCLOUD\Datos\GOES"
if not exist "C:\IGSCLOUD\Datos\import" mkdir "C:\IGSCLOUD\Datos\import"
if not exist "C:\IGSCLOUD\Datos\funvasos_inbox" mkdir "C:\IGSCLOUD\Datos\funvasos_inbox"
echo        Carpetas creadas OK

echo.

REM =====================================================================
REM  PASO 3 — Descomprimir/Copiar archivos de la aplicacion
REM =====================================================================
echo [3/7] Desplegando archivos de la aplicacion...

if exist "%ZIP_SRC%" (
    echo        Descomprimiendo publish_produccion.zip...
    powershell -NoProfile -Command ^
        "Expand-Archive -Path '%ZIP_SRC%' -DestinationPath '%SITE_PATH%' -Force"
    if %errorLevel% neq 0 (
        echo [ERROR] No se pudo descomprimir el ZIP.
        pause
        exit /b 1
    )
    echo        Archivos desplegados en %SITE_PATH%
) else (
    echo [WARN] Sin ZIP. Copie manualmente el contenido de publish_produccion\ a:
    echo        %SITE_PATH%
    pause
)

REM Verificar que el ejecutable principal existe
if not exist "%SITE_PATH%\CloudStationWeb.exe" (
    echo [WARN] CloudStationWeb.exe no encontrado en %SITE_PATH%
    echo        Verifique que el ZIP contiene los archivos correctos.
)

echo.

REM =====================================================================
REM  PASO 4 — Configurar IIS (Application Pool + Sitio)
REM =====================================================================
echo [4/7] Configurando IIS...

REM Usar appcmd (disponible en cualquier Windows con IIS)
set APPCMD=%SystemRoot%\System32\inetsrv\appcmd.exe

if not exist "%APPCMD%" (
    echo [ERROR] appcmd.exe no encontrado. Verifique que IIS esta instalado.
    pause
    exit /b 1
)

REM --- Application Pool ---
"%APPCMD%" list apppool "%APP_POOL%" >nul 2>&1
if %errorLevel% equ 0 (
    echo        App Pool '%APP_POOL%' ya existe, actualizando...
    "%APPCMD%" set apppool "%APP_POOL%" /managedRuntimeVersion:"" /pipelineMode:Integrated >nul
) else (
    echo        Creando App Pool '%APP_POOL%'...
    "%APPCMD%" add apppool /name:"%APP_POOL%" /managedRuntimeVersion:"" /pipelineMode:Integrated >nul
)
echo        App Pool OK (No Managed Code, Integrated)

REM --- Sitio Web ---
"%APPCMD%" list site "%SITE_NAME%" >nul 2>&1
if %errorLevel% equ 0 (
    echo        Sitio '%SITE_NAME%' ya existe, actualizando ruta fisica...
    "%APPCMD%" set site "%SITE_NAME%" /physicalPath:"%SITE_PATH%" >nul
    "%APPCMD%" set site "%SITE_NAME%" /applicationPool:"%APP_POOL%" >nul
) else (
    echo        Creando sitio '%SITE_NAME%'...
    "%APPCMD%" add site /name:"%SITE_NAME%" ^
        /physicalPath:"%SITE_PATH%" ^
        /bindings:"http/*:%HTTP_PORT%:" ^
        /applicationPool:"%APP_POOL%" >nul
)
echo        Sitio IIS OK (puerto %HTTP_PORT%)

REM Iniciar App Pool y Sitio
"%APPCMD%" start apppool "%APP_POOL%" >nul 2>&1
"%APPCMD%" start site "%SITE_NAME%" >nul 2>&1
echo        Sitio iniciado

echo.

REM =====================================================================
REM  PASO 5 — Permisos de carpetas para IIS App Pool
REM =====================================================================
echo [5/7] Configurando permisos de escritura para IIS...

REM El App Pool necesita permisos de escritura en estas carpetas
icacls "%SITE_PATH%"                    /grant "IIS AppPool\%APP_POOL%:(OI)(CI)RX" /T /Q >nul 2>&1
icacls "%LOG_DIR%"                      /grant "IIS AppPool\%APP_POOL%:(OI)(CI)M"  /T /Q >nul 2>&1
icacls "%KML_WWW%"                      /grant "IIS AppPool\%APP_POOL%:(OI)(CI)M"  /T /Q >nul 2>&1
icacls "%DOC_REPO%"                     /grant "IIS AppPool\%APP_POOL%:(OI)(CI)M"  /T /Q >nul 2>&1
icacls "C:\IGSCLOUD\Datos"              /grant "IIS AppPool\%APP_POOL%:(OI)(CI)M"  /T /Q >nul 2>&1
icacls "%SITE_PATH%\ImageStore"         /grant "IIS AppPool\%APP_POOL%:(OI)(CI)M"  /T /Q >nul 2>&1

REM Tambien IUSR para contenido estatico
icacls "%SITE_PATH%"                    /grant "IUSR:(OI)(CI)RX"                   /T /Q >nul 2>&1
icacls "%LOG_DIR%"                      /grant "IUSR:(OI)(CI)M"                    /T /Q >nul 2>&1

echo        Permisos configurados OK

echo.

REM =====================================================================
REM  PASO 6 — Habilitar regla de firewall HTTP
REM =====================================================================
echo [6/7] Configurando firewall Windows...

netsh advfirewall firewall show rule name="CloudStation HTTP %HTTP_PORT%" >nul 2>&1
if %errorLevel% neq 0 (
    netsh advfirewall firewall add rule ^
        name="CloudStation HTTP %HTTP_PORT%" ^
        dir=in action=allow protocol=TCP localport=%HTTP_PORT% ^
        profile=any >nul
    echo        Regla de firewall creada (TCP %HTTP_PORT% entrada)
) else (
    echo        Regla de firewall ya existe
)

echo.

REM =====================================================================
REM  PASO 7 — Reiniciar IIS y verificar
REM =====================================================================
echo [7/7] Reiniciando IIS...
iisreset /noforce >nul 2>&1
if %errorLevel% equ 0 (
    echo        IIS reiniciado OK
) else (
    echo [WARN] No se pudo reiniciar IIS automaticamente.
    echo        Ejecute manualmente: iisreset
)

REM Verificacion rapida
echo.
echo        Verificando que el sitio responde en localhost:%HTTP_PORT%...
powershell -NoProfile -Command ^
    "try { $r=(Invoke-WebRequest 'http://localhost:%HTTP_PORT%/' -UseBasicParsing -TimeoutSec 10 -ErrorAction Stop); Write-Host '       HTTP' $r.StatusCode 'OK' } catch { Write-Host '  [WARN] No responde (puede tardar ~30s en el primer arranque)' }"

echo.
echo ==============================================================
echo   INSTALACION IIS COMPLETADA
echo ==============================================================
echo.
echo   Sitio:    http://localhost:%HTTP_PORT%/
echo   Carpeta:  %SITE_PATH%
echo   App Pool: %APP_POOL%
echo   Logs:     %LOG_DIR%\stdout*.log
echo.
echo   *** PASOS SIGUIENTES (si es servidor nuevo) ***
echo.
echo   1. BASES DE DATOS (ejecutar una sola vez):
echo      SQL Server  -^> deploy_produccion_v2.sql  (contra IGSCLOUD)
echo      TimescaleDB -^> deploy_timescale_v2.sql   (contra mycloud_timescale)
echo.
echo   2. AJUSTAR CADENAS DE CONEXION:
echo      Editar: %SITE_PATH%\appsettings.json
echo      Cambiar: SqlServer, PostgreSQL segun el servidor de BD
echo.
echo   3. DAEMONS PYTHON (ejecutar como Administrador):
echo      instalar_pronostico_windows.bat    (pronostico de lluvia)
echo      instalar_mantenimiento_windows.bat (modulo mantenimiento)
echo      instalar_watchdog_tarea.bat        (recolector GOES/MyCloud)
echo.
echo   4. PYTHON MYCLOUD - setup inicial (solo primera vez):
echo      cd C:\IGSCLOUD
echo      python mycloud_all_timescale.py
echo.
echo   Consulte MANUAL_INSTALACION_PRODUCCION.md para el checklist
echo   completo y tabla de permisos de firewall.
echo ==============================================================
echo.
pause
