@echo off
REM ============================================================
REM actualizar_catalogo_reportes.bat
REM Aplica la migración del catálogo de reportes en producción
REM ============================================================
echo.
echo ============================================
echo  Actualización: Catálogo de Reportes PIH
echo ============================================
echo.

set SERVER=atlas16.ddns.net
set DB=IGSCLOUD
set USER=sa
set /p PASS="Contraseña de SQL Server (sa): "

echo.
echo [1/3] Aplicando migración SQL...
sqlcmd -S %SERVER% -d %DB% -U %USER% -P %PASS% -i "%~dp0deploy_report_catalog.sql" -b
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Falló la migración SQL.
    pause
    exit /b 1
)

echo.
echo [2/3] Publicando CloudStationWeb...
cd /d "%~dp0.."
dotnet publish CloudStationWeb\CloudStationWeb.csproj -c Release -o CloudStationWeb\publish_produccion --nologo
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Falló el publish.
    pause
    exit /b 1
)

echo.
echo [3/3] Reiniciando sitio en IIS...
%windir%\system32\inetsrv\appcmd stop site /site.name:"CloudStationWeb" 2>nul
timeout /t 3 /nobreak >nul
%windir%\system32\inetsrv\appcmd start site /site.name:"CloudStationWeb" 2>nul

echo.
echo ============================================
echo  Actualización completada exitosamente
echo ============================================
echo.
echo Endpoints disponibles:
echo   GET  /api/reports          - Catálogo público
echo   GET  /api/reports/all      - Todos (con API key)
echo   POST /api/reports          - Crear reporte
echo   PUT  /api/reports/{id}     - Actualizar reporte
echo   DELETE /api/reports/{id}   - Eliminar reporte
echo.
pause
