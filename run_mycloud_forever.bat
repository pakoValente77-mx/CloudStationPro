@echo off
:: ============================================================
:: run_mycloud_forever.bat
:: Ejecuta mycloud_all_timescale.py en bucle infinito.
:: Si el script se detiene por cualquier razón, espera 10 seg
:: y lo relanza automáticamente.
:: ============================================================
title MyCloud GOES TimescaleDB - Watchdog
cd /d C:\IGSCLOUD

:LOOP
echo [%date% %time%] Iniciando mycloud_all_timescale.py ...
python mycloud_all_timescale.py
echo.
echo [%date% %time%] El script se detuvo (exit code: %ERRORLEVEL%).
echo [%date% %time%] Reiniciando en 10 segundos...
echo [%date% %time%] Script detenido, reiniciando... >> C:\IGSCLOUD\watchdog.log
timeout /t 10 /nobreak >nul
goto LOOP
