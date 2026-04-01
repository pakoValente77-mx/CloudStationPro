@echo off
:: ============================================================
:: instalar_watchdog_tarea.bat
:: Ejecutar como Administrador.
:: Registra run_mycloud_forever.bat en el Task Scheduler
:: para que inicie automáticamente con Windows.
:: ============================================================

echo Registrando tarea programada MyCloudWatchdog...

schtasks /Create /TN "MyCloudWatchdog" ^
  /TR "C:\IGSCLOUD\run_mycloud_forever.bat" ^
  /SC ONSTART ^
  /RU SYSTEM ^
  /RL HIGHEST ^
  /F

if %ERRORLEVEL% == 0 (
    echo.
    echo =============================================
    echo   Tarea "MyCloudWatchdog" creada con exito.
    echo   Se ejecutara automaticamente al iniciar Windows.
    echo =============================================
    echo.
    echo Para iniciar ahora manualmente:
    echo   schtasks /Run /TN "MyCloudWatchdog"
    echo.
    echo Para verificar estado:
    echo   schtasks /Query /TN "MyCloudWatchdog"
    echo.
    echo Para eliminar:
    echo   schtasks /Delete /TN "MyCloudWatchdog" /F
) else (
    echo [ERROR] No se pudo crear la tarea. Ejecute como Administrador.
)

pause
