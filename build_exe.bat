@echo off
REM ============================================================
REM  build_exe.bat - Construye MyCloudTimescale.exe standalone
REM  Ejecutar UNA VEZ en cualquier Windows con internet.
REM  Despues copiar la carpeta "deploy" al servidor.
REM ============================================================

echo ====================================
echo  Construyendo MyCloudTimescale.exe
echo ====================================

REM 1. Descargar Python embebido si no hay Python instalado
where python >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] Python no encontrado. Descargando Python portable...
    curl -L -o python_embed.zip https://www.python.org/ftp/python/3.12.9/python-3.12.9-embed-amd64.zip
    echo [!] Necesitas Python instalado para usar PyInstaller.
    echo [!] Descarga Python de https://www.python.org/downloads/
    echo [!] Marca "Add Python to PATH" al instalar.
    pause
    exit /b 1
)

echo [OK] Python encontrado.
python --version

REM 2. Crear entorno virtual temporal
echo [1/4] Creando entorno virtual temporal...
python -m venv build_venv
call build_venv\Scripts\activate.bat

REM 3. Instalar dependencias
echo [2/4] Instalando dependencias...
pip install --quiet psycopg2-binary==2.9.11 schedule==1.2.2 pyodbc==5.3.0 pyinstaller==6.13.0
if %errorlevel% neq 0 (
    echo [ERROR] Fallo al instalar dependencias.
    pause
    exit /b 1
)

REM 4. Construir .exe
echo [3/4] Compilando ejecutable...
pyinstaller --clean mycloud_timescale.spec
if %errorlevel% neq 0 (
    echo [ERROR] Fallo al compilar.
    pause
    exit /b 1
)

REM 5. Crear carpeta de deploy
echo [4/4] Preparando carpeta de deploy...
if not exist deploy mkdir deploy
copy dist\MyCloudTimescale.exe deploy\
copy config.ini deploy\

REM 6. Limpiar temporales
echo Limpiando temporales...
rmdir /s /q build_venv
rmdir /s /q build
rmdir /s /q dist
rmdir /s /q __pycache__ 2>nul
del /q MyCloudTimescale.exe.manifest 2>nul

echo.
echo ====================================
echo  BUILD COMPLETADO
echo ====================================
echo.
echo Archivos en la carpeta "deploy":
dir deploy
echo.
echo Para ejecutar en el servidor:
echo   1. Copiar la carpeta "deploy" al servidor
echo   2. Asegurar que ODBC Driver 18 for SQL Server este instalado
echo   3. Editar config.ini con la configuracion correcta
echo   4. Ejecutar: MyCloudTimescale.exe
echo.
pause
