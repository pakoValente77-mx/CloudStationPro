# Guía de Generación del Ejecutable MyCloudTimescale.exe

## Requisitos previos

- **Windows 10/11 o Windows Server 2019+** (máquina de compilación)
- **Python 3.12+** instalado con "Add Python to PATH" marcado
- **Conexión a internet** (para descargar dependencias)

---

## Opción 1: Build automático con script

### Paso 1 — Copiar archivos al equipo Windows

Copia estos 4 archivos a una carpeta (ej: `C:\Build\MyCloud\`):

```
mycloud_all_timescale.py
config.ini
mycloud_timescale.spec
build_exe.bat
```

### Paso 2 — Ejecutar el build

Doble clic en `build_exe.bat` o desde CMD:

```cmd
cd C:\Build\MyCloud
build_exe.bat
```

El script hará lo siguiente automáticamente:
1. Crear un entorno virtual temporal (`build_venv`)
2. Instalar dependencias (psycopg2-binary, schedule, pyodbc, pyinstaller)
3. Compilar el ejecutable con PyInstaller
4. Crear la carpeta `deploy\` con el .exe y config.ini
5. Limpiar archivos temporales

### Paso 3 — Resultado

Al finalizar, la carpeta `deploy\` contendrá:

```
deploy\
├── MyCloudTimescale.exe    (~15-20 MB)
└── config.ini
```

---

## Opción 2: Build manual paso a paso

### Paso 1 — Abrir CMD como Administrador

```cmd
cd C:\Build\MyCloud
```

### Paso 2 — Crear entorno virtual

```cmd
python -m venv build_venv
build_venv\Scripts\activate.bat
```

### Paso 3 — Instalar dependencias

```cmd
pip install psycopg2-binary==2.9.11 schedule==1.2.2 pyodbc==5.3.0 pyinstaller==6.13.0
```

### Paso 4 — Compilar

```cmd
pyinstaller --clean mycloud_timescale.spec
```

### Paso 5 — Recoger el ejecutable

```cmd
mkdir deploy
copy dist\MyCloudTimescale.exe deploy\
copy config.ini deploy\
```

### Paso 6 — Limpiar (opcional)

```cmd
rmdir /s /q build_venv build dist __pycache__
```

---

## Despliegue en el servidor

### Requisitos del servidor destino

| Componente | Necesario | Notas |
|---|---|---|
| Python | **NO** | El .exe incluye el intérprete |
| ODBC Driver 18 | **SÍ** | Ya incluido en Windows Server 2025. En versiones anteriores: [Descargar](https://learn.microsoft.com/en-us/sql/connect/odbc/download-odbc-driver-for-sql-server) |
| .NET Runtime | **NO** | No se usa |
| Internet | **NO** | Solo necesita red local hacia SQL Server y TimescaleDB |

### Paso 1 — Copiar al servidor

Copia la carpeta `deploy\` al servidor. Ubicación recomendada:

```
C:\IGSCLOUD\MyCloudTimescale\
├── MyCloudTimescale.exe
└── config.ini
```

### Paso 2 — Configurar config.ini

Edita `config.ini` y verifica:

```ini
[timescaledb]
host = localhost          # o IP del servidor TimescaleDB
port = 5432
user = postgres
password = TuPassword
database = mycloud_timescale

[sql_server_remote]
server = localhost        # o IP del SQL Server
port = 1433
database = IGSCLOUD
user = sa
password = TuPassword

[paths]
mis_output_dir_windows = C:\IGSCLOUD\Datos\GOES

[settings]
sql_mode = remoto
reload_interval_minutes = 5
```

### Paso 3 — Ejecutar

```cmd
cd C:\IGSCLOUD\MyCloudTimescale
MyCloudTimescale.exe
```

Verás los logs en la consola:
```
[CONFIG] SQL Server Mode: REMOTO
[DB] 120 estaciones GS300 cargadas
[SENSORES] Cargados 450 sensores
[INFO] Programando descargas (+1 minuto)...
[SCHED+] E891D270 | DESFOGUE MALPASO → :32
...
[INFO] Scheduler activo...
```

### Paso 4 — Instalar como servicio de Windows (opcional)

Para que se ejecute automáticamente al iniciar el servidor, usa **NSSM** (Non-Sucking Service Manager):

1. Descarga NSSM: https://nssm.cc/download
2. Instala el servicio:

```cmd
nssm install MyCloudTimescale C:\IGSCLOUD\MyCloudTimescale\MyCloudTimescale.exe
nssm set MyCloudTimescale AppDirectory C:\IGSCLOUD\MyCloudTimescale
nssm set MyCloudTimescale DisplayName "MyCloud TimescaleDB GOES Processor"
nssm set MyCloudTimescale Description "Descarga y procesa datos GOES satelitales"
nssm set MyCloudTimescale Start SERVICE_AUTO_START
nssm set MyCloudTimescale AppStdout C:\IGSCLOUD\MyCloudTimescale\logs\service.log
nssm set MyCloudTimescale AppStderr C:\IGSCLOUD\MyCloudTimescale\logs\error.log
```

3. Iniciar el servicio:

```cmd
nssm start MyCloudTimescale
```

---

## Solución de problemas

### Error: "ODBC Driver 18 for SQL Server not found"

Instalar el driver desde:
https://learn.microsoft.com/en-us/sql/connect/odbc/download-odbc-driver-for-sql-server

### Error: "SSL Provider: unsupported protocol"

Solo ocurre en macOS/Linux con OpenSSL 3.x. En Windows no debería ocurrir porque usa SChannel nativo.

### El .exe no encuentra config.ini

Asegúrate de que `config.ini` está en la **misma carpeta** que `MyCloudTimescale.exe`.

### Error: "No se pudieron cargar estaciones"

Verifica que el SQL Server es accesible desde el servidor y que las credenciales en config.ini son correctas.

### Regenerar el .exe después de cambios al .py

Repite el proceso de build. Solo necesitas recompilar si cambias el código Python. Los cambios en `config.ini` no requieren recompilar.
