# Manual de Automatización — Catálogo de Reportes Centinela

## Índice

1. [Arquitectura](#1-arquitectura)
2. [Requisitos](#2-requisitos)
3. [Paso 1: Aplicar Migración en Producción](#3-paso-1-aplicar-migración-en-producción)
4. [Paso 2: Verificar la API del Catálogo](#4-paso-2-verificar-la-api-del-catálogo)
5. [Paso 3: Instalar ReportUploader](#5-paso-3-instalar-reportuploader)
6. [Paso 4: Configurar el Origen de Reportes](#6-paso-4-configurar-el-origen-de-reportes)
7. [Administración del Catálogo](#7-administración-del-catálogo)
8. [Agregar Nuevos Reportes](#8-agregar-nuevos-reportes)
9. [Monitoreo y Troubleshooting](#9-monitoreo-y-troubleshooting)
10. [Resumen de Archivos](#10-resumen-de-archivos)

---

## 1. Arquitectura

```
┌─────────────────────┐     cada 60s     ┌──────────────────────┐
│  Fuente de Reportes  │ ──────────────▶ │   ReportUploader      │
│  (capturas, scripts) │                 │   (C:\PIH\reportes)   │
│  genera: 1.png,      │                 └──────────┬───────────┘
│  2.png, etc.         │                            │
└─────────────────────┘                  POST /api/images/{cat}
                                                    │
                                                    ▼
                                         ┌──────────────────────┐
                                         │   CloudStationWeb     │
                                         │   (IIS / Kestrel)     │
                                         │                       │
                                         │  ImageStore/unidades/ │◀── archivos .png
                                         │  ReportDefinitions    │◀── SQL Server (catálogo)
                                         └──────────┬───────────┘
                                                    │
                                    GET /api/reports │ GET /api/images/...
                                                    ▼
                                  ┌──────────────────────────────────┐
                                  │  Clientes: Web, iOS, Android,    │
                                  │  Desktop, Bot Centinela          │
                                  └──────────────────────────────────┘
```

**Flujo:**
1. Los reportes (/1 al /7) se generan como imágenes PNG en un directorio local
2. **ReportUploader** revisa ese directorio cada 60 segundos
3. Consulta `GET /api/reports` para obtener el catálogo activo
4. Sube solo los archivos que cambiaron vía `POST /api/images/{category}`
5. Centinela lee el catálogo de la BD (cache 5 min) y sirve las imágenes

---

## 2. Requisitos

| Componente | Versión | Ubicación |
|-----------|---------|-----------|
| .NET Runtime | 8.0+ | Servidor de producción |
| SQL Server | 2016+ | atlas16.ddns.net / IGSCLOUD |
| CloudStationWeb | Última versión publicada | IIS en producción |
| sqlcmd | Cualquiera | Servidor de producción |

---

## 3. Paso 1: Aplicar Migración en Producción

### Opción A: Script SQL directo

```cmd
sqlcmd -S atlas16.ddns.net -d IGSCLOUD -U sa -P "***REDACTED-SQL-PASSWORD***" -i deploy_report_catalog.sql
```

### Opción B: Script automatizado (incluye publish + reinicio IIS)

```cmd
cd CloudStationWeb
actualizar_catalogo_reportes.bat
```

### Opción C: EF Core (desde máquina de desarrollo)

```cmd
cd CloudStationWeb
dotnet ef database update
```

### Verificar migración

```sql
SELECT * FROM ReportDefinitions ORDER BY SortOrder;
-- Debe mostrar 7 registros (/1 al /7)
```

---

## 4. Paso 2: Verificar la API del Catálogo

### Consultar catálogo público (reportes activos)

```bash
curl http://atlas16.ddns.net:5215/api/reports
```

Respuesta esperada:
```json
[
  {
    "id": 1,
    "command": "/1",
    "contentType": "image",
    "title": "Reporte de Unidades",
    "category": "unidades",
    "blobName": "9c8a7f42-3d91-4e01-a3fa-0d2e5b1c6f7d.png",
    "caption": "📊 Reporte de Unidades actualizado.",
    "isActive": true,
    "sortOrder": 1
  },
  ...
]
```

### Consultar catálogo completo (con API key)

```bash
curl -H "X-Api-Key: ***REDACTED-API-KEY***" http://atlas16.ddns.net:5215/api/reports/all
```

---

## 5. Paso 3: Instalar ReportUploader

### Opción A: Instalador automático (recomendado)

Ejecutar **como Administrador**:

```cmd
instalar_reportuploader_tarea.bat
```

Esto:
1. Compila y publica ReportUploader en `C:\PIH\ReportUploader\`
2. Crea directorio `C:\PIH\reportes\` para los archivos fuente
3. Registra tarea programada `PIH_ReportUploader` que inicia con Windows
4. Inicia el servicio inmediatamente

### Opción B: Manual

```cmd
REM Compilar
dotnet publish ReportUploader\ReportUploader.csproj -c Release -o C:\PIH\ReportUploader

REM Ejecutar
C:\PIH\ReportUploader\ReportUploader.exe ^
    --server http://atlas16.ddns.net:5215 ^
    --key ***REDACTED-API-KEY*** ^
    --watch C:\PIH\reportes ^
    --interval 60
```

### Parámetros de ReportUploader

| Parámetro | Default | Descripción |
|-----------|---------|-------------|
| `--server` | `http://localhost:5215` | URL del servidor PIH |
| `--key` | `***REDACTED-API-KEY***` | API key de ImageStore |
| `--watch` | `./reportes` | Directorio a monitorear |
| `--interval` | `60` | Segundos entre ciclos de upload |

---

## 6. Paso 4: Configurar el Origen de Reportes

El directorio `C:\PIH\reportes\` debe tener los archivos con esta nomenclatura:

```
C:\PIH\reportes\
├── 1.png                              → Comando /1 (Reporte de Unidades)
├── 2.png                              → Comando /2 (Power Monitoring)
├── 3.png                              → Comando /3 (Gráfica de Potencia)
├── 4.png                              → Comando /4 (Condición de Embalses)
├── 5.png                              → Comando /5 (Aportaciones Cuenca)
├── reporte_lluvia_1_1_TIMESTAMP.png   → Comando /6 (Lluvias 24h)
└── reporte_lluvia_1_2_TIMESTAMP.png   → Comando /7 (Lluvias parcial)
```

### Reglas de matching (prioridad)

1. **Por número de comando**: archivo `1.png` → se mapea al comando `/1`
2. **Por prefijo**: archivo `reporte_lluvia_1_1_*.png` → campo `LatestPrefix` del catálogo
3. **Por nombre exacto**: archivo `9c8a7f42-...png` → campo `BlobName` del catálogo

### Ejemplo: Script que genera un reporte cada minuto

Si tienes un script de Python o PowerShell que genera la captura:

```powershell
# Ejemplo: captura de pantalla cada 60 segundos → C:\PIH\reportes\1.png
while ($true) {
    # Tu lógica de captura aquí
    Copy-Item "C:\capturas\reporte_unidades_latest.png" "C:\PIH\reportes\1.png" -Force
    Copy-Item "C:\capturas\power_monitoring_latest.png"  "C:\PIH\reportes\2.png" -Force
    Start-Sleep -Seconds 60
}
```

ReportUploader **no resube archivos que no hayan cambiado** (compara `LastWriteTime`).

---

## 7. Administración del Catálogo

### Crear un nuevo reporte

```bash
curl -X POST http://atlas16.ddns.net:5215/api/reports \
  -H "X-Api-Key: ***REDACTED-API-KEY***" \
  -H "Content-Type: application/json" \
  -d '{
    "command": "/8",
    "contentType": "image",
    "title": "Reporte CENACE",
    "category": "unidades",
    "blobName": "reporte_cenace.png",
    "caption": "📊 Reporte CENACE actualizado.",
    "sortOrder": 8
  }'
```

### Actualizar un reporte existente

```bash
curl -X PUT http://atlas16.ddns.net:5215/api/reports/1 \
  -H "X-Api-Key: ***REDACTED-API-KEY***" \
  -H "Content-Type: application/json" \
  -d '{
    "caption": "📊 Nuevo caption para Reporte de Unidades.",
    "blobName": "nuevo_nombre.png"
  }'
```

### Desactivar un reporte (sin eliminar)

```bash
curl -X PUT http://atlas16.ddns.net:5215/api/reports/3 \
  -H "X-Api-Key: ***REDACTED-API-KEY***" \
  -H "Content-Type: application/json" \
  -d '{ "isActive": false }'
```

### Eliminar un reporte

```bash
curl -X DELETE http://atlas16.ddns.net:5215/api/reports/3 \
  -H "X-Api-Key: ***REDACTED-API-KEY***"
```

---

## 8. Agregar Nuevos Reportes

Para agregar un reporte `/9` que se actualice automáticamente:

### Paso 1: Registrar en el catálogo

```bash
curl -X POST http://atlas16.ddns.net:5215/api/reports \
  -H "X-Api-Key: ***REDACTED-API-KEY***" \
  -H "Content-Type: application/json" \
  -d '{
    "command": "/9",
    "contentType": "image",
    "title": "Mi Nuevo Reporte",
    "category": "unidades",
    "blobName": "mi_reporte.png",
    "caption": "📊 Mi nuevo reporte.",
    "sortOrder": 9
  }'
```

### Paso 2: Colocar archivo en el directorio watch

```cmd
copy mi_reporte.png C:\PIH\reportes\9.png
```

### Paso 3: Verificar

ReportUploader lo subirá en el siguiente ciclo (máximo 60 segundos). Luego:
- En Centinela: escribe `/9` → mostrará la imagen
- Desde la API: `GET /api/images/unidades/mi_reporte.png`

**No se necesita reiniciar nada.** Centinela recarga el catálogo de la BD cada 5 minutos automáticamente.

---

## 9. Monitoreo y Troubleshooting

### Ver estado de la tarea programada

```cmd
schtasks /query /tn "PIH_ReportUploader" /v
```

### Detener/Iniciar manualmente

```cmd
schtasks /end /tn "PIH_ReportUploader"
schtasks /run /tn "PIH_ReportUploader"
```

### Eliminar tarea programada

```cmd
schtasks /delete /tn "PIH_ReportUploader" /f
```

### Verificar logs de ReportUploader

La salida del programa muestra:
```
[14:30:00] Ciclo: 3 subidos, 4 sin cambios, 7 reportes en catálogo
  ✓ /1 (Reporte de Unidades) → 9c8a7f42-...-6f7d.png
  ✓ /6 (Reporte de Lluvias 24h) → reporte_lluvia_1_1_638848218556433423.png
  ✓ /7 (Reporte Parcial de Lluvias) → reporte_lluvia_1_2_638848218556433423.png
```

### Problemas comunes

| Problema | Causa | Solución |
|----------|-------|----------|
| "API key inválida" | Header `X-Api-Key` incorrecto | Verificar que sea `***REDACTED-API-KEY***` |
| "Categoría no permitida" | Categoría no existe en ImageStore | Usar: `unidades`, `charts`, `lluvia`, `general` |
| Centinela no muestra reporte nuevo | Cache de 5 min | Esperar 5 minutos o reiniciar CloudStationWeb |
| "No se pudo obtener el catálogo" | Servidor no accesible | Verificar URL y que CloudStationWeb esté corriendo |
| Archivo no se sube | No hubo cambio en `LastWriteTime` | Tocar/reescribir el archivo fuente |

### Verificar catálogo cargado en Centinela

```sql
-- En SQL Server
SELECT Command, Title, BlobName, LatestPrefix, IsActive
FROM ReportDefinitions
ORDER BY SortOrder;
```

---

## 10. Resumen de Archivos

| Archivo | Propósito |
|---------|-----------|
| `CloudStationWeb/Models/ReportDefinition.cs` | Modelo EF del catálogo |
| `CloudStationWeb/Controllers/ReportCatalogController.cs` | API CRUD `/api/reports` |
| `CloudStationWeb/Data/ApplicationDbContext.cs` | DbSet + seed data |
| `CloudStationWeb/Services/CentinelaBotService.cs` | Lee catálogo de BD (cache 5 min) |
| `CloudStationWeb/deploy_report_catalog.sql` | SQL de migración manual |
| `CloudStationWeb/actualizar_catalogo_reportes.bat` | Script todo-en-uno para producción |
| `ReportUploader/Program.cs` | Cliente auto-upload |
| `instalar_reportuploader_tarea.bat` | Instalador de tarea programada |

---

## Diagrama de Flujo Completo

```
              ┌───────────────────┐
              │ Script/Captura    │
              │ genera 1.png,     │
              │ 2.png, etc.       │
              │ cada ~1 minuto    │
              └────────┬──────────┘
                       │ escribe a disco
                       ▼
              ┌───────────────────┐
              │ C:\PIH\reportes\  │
              │ (directorio watch)│
              └────────┬──────────┘
                       │ detecta cambios
                       ▼
              ┌───────────────────┐     GET /api/reports
              │  ReportUploader   │ ◀── (catálogo dinámico)
              │  (cada 60s)       │
              └────────┬──────────┘
                       │ POST /api/images/unidades?name=X
                       ▼
              ┌───────────────────┐
              │  CloudStationWeb  │
              │  ├─ ImageStore/   │ ← archivos físicos
              │  └─ SQL Server    │ ← catálogo (ReportDefinitions)
              └────────┬──────────┘
                       │
          ┌────────────┼────────────┐
          ▼            ▼            ▼
     ┌─────────┐  ┌────────┐  ┌─────────┐
     │Centinela│  │Web/API │  │iOS/And. │
     │(/1../7) │  │clients │  │Desktop  │
     └─────────┘  └────────┘  └─────────┘
```
