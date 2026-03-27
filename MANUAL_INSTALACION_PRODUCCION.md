# CloudStation — Manual de Instalación y Despliegue en Producción

**Versión:** 2.0  
**Fecha:** 27 de marzo de 2026  
**Sistema:** CloudStation Web + Pronóstico de Lluvia  
**Plataforma:** Windows Server / IIS + Python 3.10+

---

## Tabla de Contenido

1. [Arquitectura General](#1-arquitectura-general)
2. [Requisitos del Servidor](#2-requisitos-del-servidor)
3. [URLs y Permisos de Red](#3-urls-y-permisos-de-red-para-el-administrador)
4. [Instalación de la Aplicación Web (IIS)](#4-instalación-de-la-aplicación-web-iis)
5. [Instalación del Módulo de Pronóstico de Lluvia](#5-instalación-del-módulo-de-pronóstico-de-lluvia)
6. [Instalación del Recolector GOES (MyCloud)](#6-instalación-del-recolector-goes-mycloud)
7. [Verificación Post-Instalación](#7-verificación-post-instalación)
8. [Mantenimiento y Solución de Problemas](#8-mantenimiento-y-solución-de-problemas)

---

## 1. Arquitectura General

```
┌─────────────────────────────────────────────────────────────────────┐
│                        SERVIDOR WINDOWS                            │
│                                                                     │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐  │
│  │  IIS / ASP.NET   │  │  Python Daemon   │  │  Python Daemon   │  │
│  │  CloudStation    │  │  Pronóstico      │  │  MyCloud GOES    │  │
│  │  Web (.NET 8)    │  │  (sync_rain)     │  │  (LRGS + parse)  │  │
│  └────────┬─────────┘  └────────┬─────────┘  └────────┬─────────┘  │
│           │                     │                      │            │
└───────────┼─────────────────────┼──────────────────────┼────────────┘
            │                     │                      │
     ┌──────▼──────┐       ┌─────▼──────┐        ┌──────▼──────┐
     │ SQL Server  │       │ TimescaleDB│        │  USGS LRGS  │
     │ (Catálogo)  │       │ (Series)   │        │  (GOES Sat) │
     │ Puerto 1433 │       │ Puerto 5432│        │  Puerto 16003│
     └─────────────┘       └────────────┘        └─────────────┘
```

**Componentes:**

| Componente | Tecnología | Función |
|---|---|---|
| **CloudStation Web** | ASP.NET Core 8, IIS | Portal web: mapas, gráficas, pronóstico, administración |
| **Pronóstico de Lluvia** | Python 3.10+, daemon | Descarga modelos numéricos FTP → TimescaleDB cada hora |
| **MyCloud GOES** | Python 3.10+, daemon | Recepción satelital GOES, parseo y almacenamiento |
| **SQL Server** | MSSQL 2019+ | Catálogo de estaciones, usuarios, cuencas, umbrales |
| **TimescaleDB** | PostgreSQL 16 + TimescaleDB | Series de tiempo: mediciones, pronósticos |

---

## 2. Requisitos del Servidor

### Hardware Mínimo

| Recurso | Mínimo | Recomendado |
|---|---|---|
| CPU | 4 cores | 8 cores |
| RAM | 8 GB | 16 GB |
| Disco | 100 GB SSD | 250 GB SSD |
| Red | 10 Mbps | 100 Mbps |

### Software Requerido

| Software | Versión | Descarga |
|---|---|---|
| Windows Server | 2019 o superior | — |
| IIS | 10.0+ | Rol de servidor Windows |
| .NET 8 Runtime | 8.0.x (Hosting Bundle) | https://dotnet.microsoft.com/download/dotnet/8.0 |
| Python | 3.10 o superior | https://www.python.org/downloads/ |
| SQL Server | 2019+ (ya instalado en atlas16) | — |
| PostgreSQL + TimescaleDB | 16.x (ya instalado en atlas16) | — |

> **IMPORTANTE:** Al instalar Python, marcar la casilla **"Add Python to PATH"**.

### .NET Hosting Bundle

Descargar e instalar el **ASP.NET Core 8.0 Runtime — Hosting Bundle** para Windows:
```
https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-aspnetcore-8.0.x-windows-hosting-bundle-installer
```

Después de instalar, reiniciar IIS:
```cmd
iisreset
```

---

## 3. URLs y Permisos de Red para el Administrador

### 3.1 — CRÍTICOS: Sin estos la aplicación NO funciona

| # | Destino | IP/Host | Puerto | Protocolo | Dirección | Producto | Justificación |
|---|---|---|---|---|---|---|---|
| 1 | **Base de datos PostgreSQL/TimescaleDB** | `atlas16.ddns.net` | **5432** | TCP | Servidor → BD | TimescaleDB | Almacén principal de series de tiempo (mediciones hidrológicas, pronósticos de lluvia, datos GOES). Sin acceso no hay datos para mostrar en la web. |
| 2 | **Base de datos SQL Server** | `atlas16.ddns.net` | **1433** | TCP | Servidor → BD | SQL Server IGSCLOUD | Catálogo maestro de estaciones, usuarios, autenticación, cuencas, subcuencas y permisos. Sin acceso la aplicación web no inicia. |
| 3 | **Recepción satelital GOES (primario)** | `lrgseddn1.cr.usgs.gov` | **16003** | TCP | Servidor → USGS | USGS LRGS/DDS | Canal principal de recepción de datos en tiempo real de las 100+ estaciones hidrométricas vía satélite GOES. Sin acceso no se reciben datos de campo. |
| 4 | **Recepción satelital GOES (respaldo 1)** | `lrgseddn2.cr.usgs.gov` | **16003** | TCP | Servidor → USGS | USGS LRGS/DDS | Servidor de respaldo. Si el primario falla, el sistema conmuta automáticamente a este. |
| 5 | **Recepción satelital GOES (respaldo 2)** | `lrgseddn3.cr.usgs.gov` | **16003** | TCP | Servidor → USGS | USGS LRGS/DDS | Segundo servidor de respaldo para garantizar alta disponibilidad de datos satelitales. |
| 6 | **FTP Modelos Numéricos de Pronóstico** | `200.4.8.36` | **21** + pasivos (1024-65535) | FTP | Servidor → FTP | FTP Pronóstico | Descarga de archivos CSV con el modelo numérico de pronóstico de precipitación (670 puntos grilla × 360 horas). El daemon descarga cada hora al minuto :40. Sin acceso no hay pronóstico de lluvia. |

### 3.2 — OBLIGATORIOS: Funcionalidad web completa

| # | Destino | IP/Host | Puerto | Protocolo | Dirección | Producto | Justificación |
|---|---|---|---|---|---|---|---|
| 7 | **Google Maps Tiles** | `mt0.google.com` | **80** | HTTP | Navegador → Google | Google Maps | Mapa base de terreno y satélite usado en TODOS los mapas de la aplicación (mapa general, pronóstico, administración de estaciones). Sin acceso los mapas aparecen en blanco. |
| 8 | **Google Maps Tiles (balance)** | `mt1.google.com` | **80** | HTTP | Navegador → Google | Google Maps | Subdominio de balanceo de carga para paralelizar descarga de tiles. |
| 9 | **Google Maps Tiles (balance)** | `mt2.google.com` | **80** | HTTP | Navegador → Google | Google Maps | Subdominio de balanceo de carga. |
| 10 | **Google Maps Tiles (balance)** | `mt3.google.com` | **80** | HTTP | Navegador → Google | Google Maps | Subdominio de balanceo de carga. |
| 11 | **API Condiciones Atmosféricas** | `api.open-meteo.com` | **443** | HTTPS | Servidor → API | Open-Meteo (gratuito, sin API key) | Consulta de condiciones atmosféricas en tiempo real: temperatura, humedad relativa, presión barométrica, velocidad/dirección del viento y estado del cielo. Se usa para enriquecer el boletín meteorológico con datos reales. |
| 12 | **Imagen Satelital GOES-16 (IR)** | `mesonet.agron.iastate.edu` | **443** | HTTPS (WMS) | Navegador → Iowa State | NOAA GOES-16 vía Iowa Mesonet | Imágenes satelitales GOES-16 en infrarrojo y visible en tiempo real sobre México. Se superpone como capa en el mapa de pronóstico para visualizar nubosidad actual. |
| 13 | **Radar Meteorológico (metadatos)** | `api.rainviewer.com` | **443** | HTTPS | Navegador → API | RainViewer (gratuito, sin key) | Obtiene URLs actualizadas de los tiles de radar de precipitación global en tiempo real. |
| 14 | **Radar Meteorológico (imágenes)** | `tilecache.rainviewer.com` | **443** | HTTPS | Navegador → CDN | RainViewer Tiles | Imágenes PNG del radar de precipitación que se superponen en el mapa. Muestra lluvia actual sobre la cuenca. |

### 3.3 — OPCIONALES: Degradación elegante si no hay acceso

| # | Destino | IP/Host | Puerto | Protocolo | Producto | Justificación |
|---|---|---|---|---|---|---|
| 15 | **API Histórica Open-Meteo** | `archive-api.open-meteo.com` | **443** | HTTPS | Open-Meteo Archive | Datos climatológicos ERA5 de referencia para el módulo de análisis de datos. Sin acceso, la comparación histórica no está disponible pero el resto funciona. |
| 16 | **CDN jsDelivr** | `cdn.jsdelivr.net` | **443** | HTTPS | jQuery, Fomantic UI, Chart.js | Librerías JavaScript para el panel de monitoreo Python (`monitor.html`). La aplicación web principal sirve sus librerías localmente. |
| 17 | **LRGS local CFE** | `10.41.75.100` | **16003** | TCP | LRGS local | Servidor LRGS en red interna CFE. Respaldo interno opcional. |

### 3.4 — Resumen rápido para solicitud de firewall

```
REGLAS DE SALIDA (Outbound) — SERVIDOR DE APLICACIÓN:

# Bases de datos
atlas16.ddns.net    TCP 5432    (PostgreSQL/TimescaleDB)
atlas16.ddns.net    TCP 1433    (SQL Server)

# Recepción satelital GOES
lrgseddn1.cr.usgs.gov    TCP 16003
lrgseddn2.cr.usgs.gov    TCP 16003
lrgseddn3.cr.usgs.gov    TCP 16003

# FTP Pronóstico
200.4.8.36    TCP 21 + TCP 1024-65535 (FTP pasivo)

# APIs meteorológicas (HTTPS)
api.open-meteo.com         TCP 443
archive-api.open-meteo.com TCP 443
api.rainviewer.com         TCP 443
tilecache.rainviewer.com   TCP 443
mesonet.agron.iastate.edu  TCP 443

# Google Maps (desde navegadores de los usuarios)
mt0.google.com    TCP 80
mt1.google.com    TCP 80
mt2.google.com    TCP 80
mt3.google.com    TCP 80

# CDN (opcional, solo monitor Python)
cdn.jsdelivr.net    TCP 443

REGLAS DE ENTRADA (Inbound) — SERVIDOR DE APLICACIÓN:
TCP 80/443    (IIS — acceso web de usuarios)
TCP 5555      (Panel monitor Python — solo red local)
```

---

## 4. Instalación de la Aplicación Web (IIS)

### Paso 1 — Preparar IIS

1. Abrir **Administrador del servidor** → **Agregar roles y características**
2. Habilitar:
   - **Web Server (IIS)**
   - ✅ ASP.NET 4.8
   - ✅ WebSocket Protocol
   - ✅ Static Content

3. Instalar el **.NET 8 Hosting Bundle** (ver sección 2)

### Paso 2 — Crear el sitio

1. Copiar la carpeta `publicado_ok\` al servidor, por ejemplo:
   ```
   C:\inetpub\CloudStation\
   ```

2. En **IIS Manager**:
   - Click derecho en **Sites** → **Add Website**
   - **Site name:** `CloudStation`
   - **Physical path:** `C:\inetpub\CloudStation`
   - **Binding:** Puerto 80 (o 443 con certificado SSL)

3. Configurar el **Application Pool**:
   - Click derecho en el App Pool → **Basic Settings**
   - **.NET CLR version:** `No Managed Code`
   - **Pipeline:** `Integrated`

### Paso 3 — Verificar web.config

El archivo `web.config` ya incluye los MIME types necesarios para KML:

```xml
<staticContent>
    <remove fileExtension=".kml" />
    <mimeMap fileExtension=".kml" mimeType="application/vnd.google-earth.kml+xml" />
    <remove fileExtension=".kmz" />
    <mimeMap fileExtension=".kmz" mimeType="application/vnd.google-earth.kmz" />
    <remove fileExtension=".json" />
    <mimeMap fileExtension=".json" mimeType="application/json" />
</staticContent>
```

### Paso 4 — Configurar cadenas de conexión

Editar `appsettings.json` en la carpeta del sitio:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=atlas16.ddns.net;Database=IGSCLOUD;User Id=sa;Password=***REDACTED-SQL-PASSWORD***;TrustServerCertificate=True;",
    "PostgreSQL": "Host=atlas16.ddns.net;Port=5432;Database=mycloud_timescale;Username=postgres;Password=***REDACTED-PG-PASSWORD***;"
  }
}
```

### Paso 5 — Crear carpetas necesarias

```cmd
mkdir C:\inetpub\CloudStation\wwwroot\kml
mkdir C:\inetpub\CloudStation\logs
mkdir C:\IGSCLOUD\DocumentRepository\boletin
mkdir C:\IGSCLOUD\DocumentRepository\funvasos
```

### Paso 6 — Permisos de carpeta

El App Pool de IIS necesita permisos de escritura en:

```
C:\inetpub\CloudStation\wwwroot\kml        (subida de KMLs)
C:\inetpub\CloudStation\logs               (logs de ASP.NET)
C:\IGSCLOUD\DocumentRepository\            (repositorio de documentos)
```

Click derecho → Propiedades → Seguridad → Agregar → `IIS AppPool\CloudStation` → Permisos: **Modificar**.

### Paso 7 — Verificar

Abrir en navegador: `http://localhost/`

Debería mostrar la página de login de CloudStation.

---

## 5. Instalación del Módulo de Pronóstico de Lluvia

### Método automático (recomendado)

1. Copiar estos archivos a una carpeta temporal en el servidor:
   ```
   instalar_pronostico_windows.bat
   setup_inicial_pronostico.bat
   sync_rain_forecast.py
   setup_rain_forecast.py
   config.ini
   requirements.txt
   CloudStationWeb\KML Cuencas y Rios Grijalva\*.kml
   ```

2. **Click derecho** en `instalar_pronostico_windows.bat` → **Ejecutar como administrador**

   Esto automáticamente:
   - Crea la carpeta `C:\IGSCLOUD\RainForecast\`
   - Copia todos los archivos incluyendo KMLs
   - Instala las dependencias Python (psycopg2, shapely, schedule)
   - Crea la tarea programada `CloudStation_SyncRainForecast`

3. **Solo la primera vez**, ejecutar el setup de base de datos:
   ```cmd
   cd C:\IGSCLOUD\RainForecast
   python setup_inicial_pronostico.bat
   ```
   
   O manualmente:
   ```cmd
   cd C:\IGSCLOUD\RainForecast
   python setup_rain_forecast.py
   python sync_rain_forecast.py
   ```

   Esto crea:
   - Esquema `rain_forecast` en TimescaleDB
   - Tablas: `forecast`, `subcuenca_poligono`, `rain_record` (hypertable)
   - Vista: `resumen_horario_pronostico`
   - Carga 42 polígonos de subcuencas desde los KML
   - Ejecuta la primera descarga de pronóstico del día

### Método manual

```cmd
:: 1. Crear directorio
mkdir C:\IGSCLOUD\RainForecast
mkdir C:\IGSCLOUD\RainForecast\logs
mkdir "C:\IGSCLOUD\RainForecast\CloudStationWeb\KML Cuencas y Rios Grijalva"

:: 2. Copiar archivos
copy sync_rain_forecast.py C:\IGSCLOUD\RainForecast\
copy setup_rain_forecast.py C:\IGSCLOUD\RainForecast\
copy config.ini C:\IGSCLOUD\RainForecast\
copy "CloudStationWeb\KML Cuencas y Rios Grijalva\*.kml" "C:\IGSCLOUD\RainForecast\CloudStationWeb\KML Cuencas y Rios Grijalva\"

:: 3. Instalar dependencias
python -m pip install psycopg2-binary shapely schedule

:: 4. Setup inicial (una sola vez)
cd C:\IGSCLOUD\RainForecast
python setup_rain_forecast.py

:: 5. Primera sincronización
python sync_rain_forecast.py

:: 6. Crear tarea programada
schtasks /create /tn "CloudStation_SyncRainForecast" /tr "python C:\IGSCLOUD\RainForecast\sync_rain_forecast.py --daemon" /sc ONSTART /ru SYSTEM /rl HIGHEST /f
```

### Verificar que el daemon está corriendo

```cmd
:: Ver si la tarea existe
schtasks /query /tn "CloudStation_SyncRainForecast"

:: Iniciar manualmente la tarea
schtasks /run /tn "CloudStation_SyncRainForecast"

:: O ejecutar directamente para ver salida
cd C:\IGSCLOUD\RainForecast
python sync_rain_forecast.py --daemon
```

El daemon:
- Se ejecuta al arrancar Windows automáticamente
- Descarga el pronóstico del día cada hora al minuto **:40**
- Si el archivo ya fue descargado, no lo re-procesa
- Los datos cubren **360 horas** (15 días) de pronóstico

---

## 6. Instalación del Recolector GOES (MyCloud)

El daemon `mycloud_all_timescale.py` ya debería estar instalado. Si no:

```cmd
:: Crear directorio
mkdir C:\IGSCLOUD\Datos\GOES
mkdir C:\IGSCLOUD\Datos\import
mkdir C:\IGSCLOUD\Datos\funvasos_inbox

:: Copiar archivos
copy mycloud_all_timescale.py C:\IGSCLOUD\
copy config.ini C:\IGSCLOUD\

:: Instalar dependencias
python -m pip install -r requirements.txt

:: Crear tarea programada
schtasks /create /tn "CloudStation_MyCloud" /tr "python C:\IGSCLOUD\mycloud_all_timescale.py" /sc ONSTART /ru SYSTEM /rl HIGHEST /f
```

### Credenciales LRGS

Las credenciales están en `config.ini`:

```ini
[lrgs]
default_user = cilasur
default_password = ksip-CWNG-05
hosts = lrgseddn1.cr.usgs.gov,lrgseddn2.cr.usgs.gov,lrgseddn3.cr.usgs.gov
```

---

## 7. Verificación Post-Instalación

### Checklist

| # | Verificación | Comando/URL | Esperado |
|---|---|---|---|
| 1 | Sitio web responde | `http://localhost/` | Página de login |
| 2 | Login funciona | Ingresar credenciales | Dashboard |
| 3 | Mapa carga tiles | Navegar a Mapa | Mapa con estaciones |
| 4 | KMLs se sirven | `http://localhost/kml/Red_Rios_Cuencas_Grijalva.kml` | Descarga KML (no 404) |
| 5 | Pronóstico de lluvia | Navegar a Pronóstico | Tarjetas de cuencas con datos |
| 6 | Boletín meteorológico | Parte inferior de Pronóstico | Texto con condiciones atmosféricas |
| 7 | Capas satelitales | Activar GOES-16 IR en mapa | Imagen de nubosidad visible |
| 8 | Base capa satélite | Cambiar a "Satélite" en control de capas | Vista satelital Google |
| 9 | Datos GOES llegan | Verificar tabla estaciones | Datos recientes (<1h) |
| 10 | Daemon pronóstico | `schtasks /query /tn "CloudStation_SyncRainForecast"` | Estado: Running |

### Test de conectividad desde el servidor

Ejecutar estos comandos en CMD del servidor para verificar que las URLs son alcanzables:

```cmd
@echo off
echo === Test de Conectividad CloudStation ===
echo.

echo [1] SQL Server (atlas16:1433)...
powershell -Command "Test-NetConnection atlas16.ddns.net -Port 1433" | findstr "TcpTestSucceeded"

echo [2] PostgreSQL (atlas16:5432)...
powershell -Command "Test-NetConnection atlas16.ddns.net -Port 5432" | findstr "TcpTestSucceeded"

echo [3] LRGS USGS 1 (16003)...
powershell -Command "Test-NetConnection lrgseddn1.cr.usgs.gov -Port 16003" | findstr "TcpTestSucceeded"

echo [4] LRGS USGS 2 (16003)...
powershell -Command "Test-NetConnection lrgseddn2.cr.usgs.gov -Port 16003" | findstr "TcpTestSucceeded"

echo [5] LRGS USGS 3 (16003)...
powershell -Command "Test-NetConnection lrgseddn3.cr.usgs.gov -Port 16003" | findstr "TcpTestSucceeded"

echo [6] FTP Pronostico (200.4.8.36:21)...
powershell -Command "Test-NetConnection 200.4.8.36 -Port 21" | findstr "TcpTestSucceeded"

echo [7] Open-Meteo API (443)...
powershell -Command "Test-NetConnection api.open-meteo.com -Port 443" | findstr "TcpTestSucceeded"

echo [8] RainViewer API (443)...
powershell -Command "Test-NetConnection api.rainviewer.com -Port 443" | findstr "TcpTestSucceeded"

echo [9] GOES-16 Iowa Mesonet (443)...
powershell -Command "Test-NetConnection mesonet.agron.iastate.edu -Port 443" | findstr "TcpTestSucceeded"

echo [10] Google Maps (80)...
powershell -Command "Test-NetConnection mt0.google.com -Port 80" | findstr "TcpTestSucceeded"

echo.
echo === Todos los resultados deben decir: TcpTestSucceeded : True ===
pause
```

---

## 8. Mantenimiento y Solución de Problemas

### Logs

| Log | Ubicación | Contenido |
|---|---|---|
| IIS / ASP.NET | `C:\inetpub\CloudStation\logs\stdout_*.log` | Errores de la web |
| Daemon Pronóstico | Salida estándar de la tarea | Descargas FTP, procesamiento |
| MyCloud GOES | Salida estándar de la tarea | Recepción LRGS, parseo |

Para habilitar logs de ASP.NET, en `web.config` cambiar:
```xml
<aspNetCore ... stdoutLogEnabled="true" ... />
```

### Problemas comunes

| Problema | Causa | Solución |
|---|---|---|
| Mapas en blanco | Firewall bloquea `mt0-mt3.google.com:80` | Agregar regla de salida HTTP |
| KML devuelve 404 | MIME type no registrado en IIS | Verificar `<staticContent>` en web.config |
| Pronóstico sin datos | FTP bloqueado o daemon no corre | Verificar tarea programada y acceso a `200.4.8.36:21` |
| Boletín sin condiciones atmosféricas | API Open-Meteo bloqueada | Verificar acceso HTTPS a `api.open-meteo.com` |
| GOES-16 no se ve en mapa | WMS de Iowa bloqueado | Verificar HTTPS a `mesonet.agron.iastate.edu` |
| App no inicia / error 500 | SQL Server o PostgreSQL inaccesible | Verificar puertos 1433 y 5432 a `atlas16.ddns.net` |
| No llegan datos GOES | LRGS USGS bloqueado | Verificar puerto 16003 a `lrgseddn*.cr.usgs.gov` |
| Error "connection refused" en FTP | Puertos pasivos bloqueados | Abrir rango 1024-65535 hacia `200.4.8.36` |

### Reiniciar servicios

```cmd
:: Reiniciar IIS
iisreset

:: Reiniciar daemon de pronóstico
schtasks /end /tn "CloudStation_SyncRainForecast"
schtasks /run /tn "CloudStation_SyncRainForecast"

:: Reiniciar daemon MyCloud
schtasks /end /tn "CloudStation_MyCloud"
schtasks /run /tn "CloudStation_MyCloud"
```

### Actualizar la aplicación web

1. Detener el sitio en IIS (o el App Pool)
2. Copiar los nuevos archivos de `publicado_ok\` a `C:\inetpub\CloudStation\`
3. Iniciar el sitio en IIS
4. Verificar en navegador

### Actualizar el daemon de pronóstico

```cmd
:: Detener daemon
schtasks /end /tn "CloudStation_SyncRainForecast"

:: Copiar nuevo script
copy /Y sync_rain_forecast.py C:\IGSCLOUD\RainForecast\

:: Reiniciar
schtasks /run /tn "CloudStation_SyncRainForecast"
```

---

## Estructura de Archivos en Producción

```
C:\inetpub\CloudStation\              ← Sitio web IIS
├── CloudStationWeb.exe
├── CloudStationWeb.dll
├── web.config                         ← Con MIME types KML
├── appsettings.json                   ← Cadenas de conexión
├── wwwroot\
│   ├── kml\                           ← KMLs de cuencas
│   └── lib\                           ← jQuery, Fomantic, Highcharts, Leaflet
└── logs\

C:\IGSCLOUD\
├── RainForecast\                      ← Módulo de pronóstico
│   ├── sync_rain_forecast.py          ← Daemon (--daemon)
│   ├── setup_rain_forecast.py         ← Setup BD (una vez)
│   ├── config.ini                     ← Credenciales
│   ├── iniciar_sync.bat               ← Inicio manual
│   └── CloudStationWeb\
│       └── KML Cuencas y Rios Grijalva\  ← Polígonos subcuencas
│           ├── Subcuencas_ANG.kml
│           ├── Subcuencas_MMT.kml
│           ├── Subcuencas_MPS.kml
│           └── Subcuencas_PEA.kml
├── Datos\
│   ├── GOES\                          ← Archivos MIS descargados
│   ├── import\                        ← Importación manual
│   └── funvasos_inbox\                ← Archivos FunVasos
├── DocumentRepository\
│   ├── boletin\                       ← Boletines PDF
│   └── funvasos\                      ← Reportes FunVasos
├── mycloud_all_timescale.py           ← Daemon GOES
└── config.ini
```

---

**Fin del documento.**

Para dudas o soporte técnico, contactar al equipo de desarrollo de la Subgerencia Grijalva.
