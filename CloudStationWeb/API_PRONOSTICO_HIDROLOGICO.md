# API de Pronóstico Hidrológico y Estaciones — CloudStation

## Información General

| Campo | Valor |
|-------|-------|
| **Base URL** | `https://hidrometria.mx` |
| **Autenticación** | Header `X-Api-Key` ó JWT Bearer con rol ApiConsumer |
| **Formato** | JSON (UTF-8) |
| **Modelo** | HUI (Hidrograma Unitario Instantáneo) + SCS-NRCS |
| **Presas** | Angostura → Chicoasén → Malpaso → Tapón Juan Grijalva → Peñitas |
| **Compatibilidad** | grijalva-hydro-model-service, grijalva-core-service, grijalva-automatic-station-service, grijalva-rain-forecast-service (Spring Boot) |

---

## Autenticación

La API soporta dos métodos de autenticación:

### Método 1: API Key (Header)
```bash
curl -H "X-Api-Key: ***REDACTED-API-KEY***" http://servidor:5215/api/hydro/dams
```

### Método 2: JWT Bearer Token
El usuario debe tener el rol **ApiConsumer**, **SuperAdmin** o **Administrador**.
```bash
curl -H "Authorization: Bearer eyJhbGciOi..." http://servidor:5215/api/hydro/dams
```

---

## Roles con Acceso a la API

| Rol | Acceso Web | Acceso API | Descripción |
|-----|-----------|------------|-------------|
| **SuperAdmin** | ✓ Completo | ✓ | Control total del sistema |
| **Administrador** | ✓ Gestión | ✓ | Administración de estaciones y usuarios |
| **ApiConsumer** | ✗ | ✓ | Consumo exclusivo de API REST |
| Operador | ✓ Operativo | ✗ | Carga de datos y operación diaria |
| Visualizador | ✓ Lectura | ✗ | Solo visualización de datos |
| SoloVasos | ✓ FunVasos | ✗ | Acceso limitado a FunVasos |

---

## Endpoints

### 1. Catálogo de Presas

```
GET /api/hydro/dams
```

Devuelve las 5 presas con sus niveles de referencia. Formato compatible con `Dam.java` de Spring Boot.

**Ejemplo:**
```bash
curl -H "X-Api-Key: ***REDACTED-API-KEY***" http://servidor:5215/api/hydro/dams
```

**Respuesta:**
```json
[
  {
    "id": 1,
    "centralId": 1,
    "code": "ANG",
    "description": "Angostura",
    "nameValue": 542.1,
    "namoValue": 539.0,
    "naminoValue": 510,
    "hasPreviousDam": false,
    "modelType": "HUI"
  },
  {
    "id": 2,
    "centralId": 2,
    "code": "CHI",
    "description": "Chicoasén",
    "nameValue": 400.0,
    "namoValue": 395.0,
    "naminoValue": 378,
    "hasPreviousDam": true,
    "modelType": "HUI"
  }
]
```

---

### 2. Datos de Entrada del Modelo

```
GET /api/hydro/input?horizonHours=72
```

Devuelve las condiciones iniciales reales (último dato de FunVasos) y el pronóstico de lluvia por cuenca que alimentan la simulación.

| Parámetro | Tipo | Default | Rango | Descripción |
|-----------|------|---------|-------|-------------|
| `horizonHours` | int | 72 | 1–360 | Horas de pronóstico a consultar |

**Ejemplo:**
```bash
curl -H "X-Api-Key: ***REDACTED-API-KEY***" \
  "http://servidor:5215/api/hydro/input?horizonHours=72"
```

**Respuesta:**
```json
{
  "success": true,
  "forecastDate": "2026-04-10",
  "horizonHours": 72,
  "dams": [
    {
      "damName": "Angostura",
      "cuencaCode": "ang",
      "elevacion": 532.15,
      "almacenamiento": 12500.30,
      "aportacionQ": 45.20,
      "extraccionQ": 120.50,
      "fechaBase": "2026-04-10",
      "ultimaHora": 18,
      "totalRainMm": 35.80,
      "curveNumber": 75,
      "drainCoeff": 0.15
    }
  ],
  "rainByCuenca": [
    {
      "cuencaCode": "ang",
      "hours": [
        { "time": "2026-04-10 19:00", "rainMm": 2.50 },
        { "time": "2026-04-10 20:00", "rainMm": 1.80 }
      ]
    }
  ]
}
```

---

### 3. Ejecutar Simulación

```
POST /api/hydro/simulate
```

Ejecuta el modelo hidrológico completo (balance hídrico hora a hora en cascada). Permite editar extracciones, aportaciones y parámetros del modelo.

**Headers:**
```
Content-Type: application/json
X-Api-Key: ***REDACTED-API-KEY***
```

**Body:**
```json
{
  "horizonHours": 72,
  "extractions": {
    "Angostura": 120.5,
    "Chicoasen": 200.0,
    "Malpaso": 180.0,
    "JGrijalva": 0,
    "Penitas": 150.0
  },
  "extractionSchedule": null,
  "aportationSchedule": null,
  "drainCoefficients": null,
  "curveNumbers": null
}
```

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `horizonHours` | int | Horas de pronóstico (1–360) |
| `extractions` | dict | Extracción constante por presa (m³/s). Opcional. |
| `extractionSchedule` | dict | Extracción variable por día: `{ "Angostura": [120, 130, 125] }` (m³/s). Tiene prioridad sobre `extractions`. |
| `aportationSchedule` | dict | Aportación manual por día (m³/s). Reemplaza la calculada por lluvia. |
| `drainCoefficients` | dict | Coeficiente de escurrimiento por presa. Opcional. |
| `curveNumbers` | dict | Curva Number SCS por presa. Opcional. |

**Ejemplo mínimo (usa valores por defecto):**
```bash
curl -X POST -H "X-Api-Key: ***REDACTED-API-KEY***" \
  -H "Content-Type: application/json" \
  -d '{"horizonHours": 72}' \
  http://servidor:5215/api/hydro/simulate
```

**Respuesta (formato HydroModel — compatible Spring Boot):**
```json
[
  {
    "subBasinId": 1,
    "modelType": "HUI",
    "date": "2026-04-10",
    "dam": {
      "id": 1,
      "centralId": 1,
      "code": "ANG",
      "description": "Angostura",
      "nameValue": 542.1,
      "namoValue": 539.0,
      "naminoValue": 510,
      "hasPreviousDam": false
    },
    "records": [
      {
        "date": "2026-04-10",
        "rain": 2.50,
        "extractionPreviousDam": 0.0000,
        "elevation": 532.18,
        "extraction": 0.0120,
        "basinInput": 0.0150,
        "totalCapacity": 12505.20,
        "forecast": true,
        "hour": 1
      }
    ]
  }
]
```

#### Campos de cada registro (`records`) — HydroModelRecord

| Campo | Unidad | Descripción |
|-------|--------|-------------|
| `date` | ISO 8601 | Fecha del registro |
| `rain` | mm | Precipitación promedio de la subcuenca |
| `extractionPreviousDam` | Mm³/h | Aportación de la presa aguas arriba |
| `elevation` | msnm | Nivel del embalse calculado |
| `extraction` | Mm³/h | Extracción (turbinado + vertido) |
| `basinInput` | Mm³/h | Aportación por escurrimiento de cuenca propia |
| `totalCapacity` | Mill. m³ | Almacenamiento total |
| `forecast` | bool | `true` = pronóstico, `false` = dato real |
| `hour` | int | Hora secuencial desde el inicio (1, 2, ...) |

---

### 4. Tendencia (Datos Reales + Pronóstico)

```
GET /api/hydro/trend?realDays=5&forecastHours=72
```

Combina datos históricos reales de FunVasos con el pronóstico del modelo en una sola serie temporal. Ideal para gráficas de tendencia.

| Parámetro | Tipo | Default | Rango | Descripción |
|-----------|------|---------|-------|-------------|
| `realDays` | int | 5 | 1–30 | Días de datos reales hacia atrás |
| `forecastHours` | int | 72 | 1–360 | Horas de pronóstico hacia adelante |

**Ejemplo:**
```bash
curl -H "X-Api-Key: ***REDACTED-API-KEY***" \
  "http://servidor:5215/api/hydro/trend?realDays=5&forecastHours=120"
```

**Respuesta (array de HydroModel con records reales + pronóstico):**
```json
[
  {
    "subBasinId": 1,
    "modelType": "HUI",
    "date": "2026-04-10",
    "dam": {
      "id": 1,
      "centralId": 1,
      "code": "ANG",
      "description": "Angostura",
      "nameValue": 542.1,
      "namoValue": 539.0,
      "naminoValue": 510,
      "hasPreviousDam": false
    },
    "records": [
      {
        "date": "2026-04-05",
        "rain": 0.0,
        "extractionPreviousDam": 0.0,
        "elevation": 531.50,
        "extraction": 120.50,
        "basinInput": 45.20,
        "totalCapacity": 12400.00,
        "forecast": false,
        "hour": 1
      },
      {
        "date": "2026-04-10",
        "rain": 2.50,
        "extractionPreviousDam": 0.0000,
        "elevation": 532.18,
        "extraction": 0.0120,
        "basinInput": 0.0150,
        "totalCapacity": 12505.20,
        "forecast": true,
        "hour": 1
      }
    ]
  }
]
```

#### Campos de cada punto de serie (records)

| Campo | Unidad | Descripción |
|-------|--------|-------------|
| `date` | ISO 8601 | Fecha del registro |
| `elevation` | msnm | Nivel del embalse |
| `totalCapacity` | Mill. m³ | Almacenamiento total |
| `basinInput` | m³/s ó Mm³/h | Gasto de aportación |
| `extraction` | m³/s ó Mm³/h | Gasto de extracción total |
| `forecast` | bool | `false` = dato real, `true` = pronóstico calculado |

---

## Niveles de Referencia

| Nivel | Significado | Uso |
|-------|-------------|-----|
| **NAMO** | Nivel de Aguas Máximas Ordinarias | Operación normal. Asíntota amarilla en gráficas. |
| **NAME** | Nivel de Aguas Máximas Extraordinarias | Emergencia. Asíntota roja en gráficas. |
| **NAMINO** | Nivel Mínimo de Operación | Límite inferior. |

---

## Modelo Hidrológico

El pronóstico usa un **modelo concentrado** con los siguientes componentes:

1. **Lluvia Efectiva (SCS-NRCS):** $PE = \frac{(P - 0.2S)^2}{P + 0.8S}$, donde $S = \frac{25400}{CN} - 254$
2. **Convolución HUI:** El escurrimiento se calcula como la convolución de PE con el Hidrograma Unitario Instantáneo de la subcuenca.
3. **Balance Hídrico:** $V(t) = V(t-1) + Q_{cuenca} + Q_{upstream} - Q_{extracción}$
4. **Cascada:** Las extracciones de una presa llegan como aportación a la siguiente (con desfase temporal).

---

## Errores

| HTTP | Significado |
|------|-------------|
| 401 | API key inválida o faltante |
| 400 | Parámetros fuera de rango |
| 500 | Error interno (base de datos, cálculo) |

Formato de error:
```json
{ "error": "API key inválida" }
```

---

## Ejemplo Completo: Python

```python
import requests

BASE = "http://atlas16.ddns.net:5215"
HEADERS = {"X-Api-Key": "***REDACTED-API-KEY***"}

# 1. Obtener tendencia (5 días reales + 3 días pronóstico)
resp = requests.get(f"{BASE}/api/hydro/trend", headers=HEADERS,
                    params={"realDays": 5, "forecastHours": 72})
models = resp.json()  # Array de HydroModel

for model in models:
    dam = model["dam"]
    print(f"\n=== {dam['description']} ({dam['code']}) ===")
    print(f"  NAMO: {dam['namoValue']} | NAME: {dam['nameValue']}")
    
    reales = [r for r in model["records"] if not r["forecast"]]
    pronostico = [r for r in model["records"] if r["forecast"]]
    print(f"  Puntos reales: {len(reales)}, pronóstico: {len(pronostico)}")
```

## Ejemplo Completo: C#

```csharp
using var http = new HttpClient();
http.DefaultRequestHeaders.Add("X-Api-Key", "***REDACTED-API-KEY***");

// Simulación con extracciones personalizadas
var request = new {
    horizonHours = 72,
    extractions = new Dictionary<string, double> {
        ["Angostura"] = 120, ["Chicoasen"] = 200,
        ["Malpaso"] = 180, ["Penitas"] = 150
    }
};

var json = JsonSerializer.Serialize(request);
var content = new StringContent(json, Encoding.UTF8, "application/json");
var resp = await http.PostAsync("http://servidor:5215/api/hydro/simulate", content);
var result = await resp.Content.ReadAsStringAsync();
Console.WriteLine(result);
```

---

## Endpoints de Estaciones (core-service compatible)

Estos endpoints replican exactamente las URLs del `grijalva-core-service` (Spring Boot) para que la migración sea transparente al cliente. Solo se cambia la base URL.

---

### 5. Buscar Estación por Central / Clase / Tipo

```
GET /api/get/station/by/central-id/{centralId}/class/{clazz}/type/{stationType}
```

| Parámetro | Valores | Descripción |
|-----------|---------|-------------|
| `centralId` | 1–5 | ID de la central hidroeléctrica |
| `clazz` | `A` (Automática), `C` (Convencional) | Clase de estación |
| `stationType` | `E` (Embalse), `H` (Hidrométrica) | Tipo de estación |

**Ejemplo:**
```bash
curl -H "X-Api-Key: ***REDACTED-API-KEY***" \
  https://hidrometria.mx/api/get/station/by/central-id/1/class/A/type/E
```

**Respuesta:**
```json
{
  "id": 2,
  "vendorId": "ANG-E-A",
  "centralId": 1,
  "code": "ANG-E-A",
  "assignedId": "ANG-02",
  "name": "Angostura Automática",
  "clazz": "A",
  "type": "E",
  "subBasinId": 1,
  "weighingInput": 0.15,
  "latitude": "16.848",
  "longitude": "-93.535"
}
```

---

### 6. Estaciones para Modelo Hidrológico por Subcuenca

```
GET /api/get/station/hydro-model/by/sub-basin/{subBasinId}
```

Retorna todas las estaciones de una subcuenca usadas por el modelo.

**Ejemplo:**
```bash
curl -H "X-Api-Key: ***REDACTED-API-KEY***" \
  https://hidrometria.mx/api/get/station/hydro-model/by/sub-basin/1
```

---

### 7. Presa por ID

```
GET /api/get/dam/by/id/{damId}
```

**Respuesta:**
```json
{
  "id": 1,
  "centralId": 1,
  "code": "ANG",
  "description": "Angostura",
  "nameValue": 542.1,
  "namoValue": 539.0,
  "naminoValue": 510,
  "usefulVolume": 11115.0,
  "offVolume": 6554.0,
  "totalVolume": 17669.0,
  "inputArea": 22000.0,
  "hasPreviousDam": false,
  "huiFactor": 1.0,
  "modelType": "daily"
}
```

---

### 8. Presa por Central

```
GET /api/get/dam/by/central/{centralId}
```

---

### 9. Subcuenca con HUI

```
GET /api/get/sub-basin/by/id/{id}
```

**Respuesta:**
```json
{
  "id": 1,
  "idCuenca": 1,
  "clave": "ANG",
  "nombre": "Angostura",
  "inputFactor": 0.15,
  "transferTime": 0,
  "hoursRead": [6, 12, 18, 24],
  "hui": [0.05, 0.10, 0.20, 0.25, 0.20, 0.10, 0.05, 0.03, 0.02],
  "previousDaysNumber": null
}
```

---

### 10. Central Hidroeléctrica

```
GET /api/get/central/by/id/{id}
```

**Respuesta:**
```json
{
  "id": 1,
  "previousCentralId": null,
  "idCuenca": 1,
  "idSubcuenca": 1,
  "clave20": "ANG",
  "claveCenace": "K02",
  "claveSap": "ANG",
  "nombre": "C.H. Angostura",
  "unidades": 5,
  "capacidadInstalada": 900,
  "consumoEspecifico": 4.1,
  "latitud": 16.848,
  "longitud": -93.535,
  "orden": 1
}
```

---

### 11. Curva Elevación-Capacidad por Elevación

```
GET /api/get/elevation-capacity/by/central/{centralId}/elevation/{elevation}
```

Interpola la capacidad (Mill. m³) para una elevación dada (msnm).

---

### 12. Curva Elevación-Capacidad por Capacidad

```
GET /api/get/elevation-capacity/by/central/{centralId}/capacity/{capacity}
```

Interpola la elevación (msnm) para una capacidad dada (Mill. m³).

---

### 13. Registro Horario Convencional

```
GET /api/get/station-report/records/by/station/{stationId}/date/{date}/hour/{hour}
```

Datos horarios de FunVasos: elevación, almacenamiento, generación, gasto.

**Respuesta:**
```json
{
  "id": 1,
  "hour": 6,
  "elevation": 532.15,
  "scale": 12500.3,
  "powerGeneration": 150.0,
  "spent": 120.5,
  "precipitation": null,
  "unitsWorking": 3
}
```

---

### 14. Comportamiento de Presa (día completo)

```
GET /api/get/dam-behavior/central-id/{centralId}/date/{date}
```

Retorna 24 registros (uno por hora) con el comportamiento operativo completo de la presa.

**Respuesta:**
```json
[
  {
    "dateTime": "2026-04-10T06:00:00",
    "hour": 6,
    "elevation": 532.15,
    "utilCapacity": 12500.3,
    "diffCapacity": -5.2,
    "inputSpending": 45.20,
    "inputVolume": 0.163,
    "turbineSpending": 100.0,
    "turbineVolume": 0.360,
    "chuteSpending": 20.5,
    "chuteVolume": 0.074,
    "totalSpending": 120.5,
    "totalVolume": 0.434,
    "generation": 150.0,
    "unitsWorking": 3,
    "inputAverage": 42.8
  }
]
```

---

### 15. Gasto de Flujo Primario

```
GET /api/get/dam-behavior/primary-flow-spending/by/central-id/{centralId}/date/{date}/hour/{hour}
```

Retorna un valor decimal: el gasto total de extracción (m³/s) a esa hora.

---

## Endpoints de Estaciones Automáticas (automatic-stations-connector compatible)

### 16. Lluvia Acumulada por ID

```
GET /api/get/accumulative-rain/by/id/{stationId}/date/{date}/hour/{hour}
```

**Respuesta:**
```json
{
  "dateTime": "2026-04-10T18:00:00",
  "rain": 15.60
}
```

---

### 17. Lluvia Acumulada por AssignedId + VendorId

```
GET /api/get/accumulative-rain/by/assignedId/{assignedId}/vendorId/{vendorId}/date/{date}/hour/{hour}
```

---

## Endpoints de Pronóstico de Lluvia (rain-forecast-service compatible)

### 18. Último Pronóstico Disponible

```
GET /v1/forecast/last
```

**Respuesta:**
```json
[
  {
    "id": "uuid",
    "date": "2026-04-10",
    "timestamp": "2026-04-10T12:00:00Z",
    "lastUpdate": "2026-04-10T12:00:00Z"
  }
]
```

---

### 19. Pronóstico por Fecha

```
GET /v1/forecast/date/{date}
```

---

### 20. Registros de Lluvia Pronosticada

```
GET /v1/record/forecast-date/{date}/sub-basin-id/{subBasinId}/dates/{startIsoDate}/{endIsoDate}
```

| Parámetro | Formato | Descripción |
|-----------|---------|-------------|
| `date` | `yyyy-MM-dd` | Fecha del pronóstico |
| `subBasinId` | 1–5 | ID de subcuenca |
| `startIsoDate` | ISO 8601 | Inicio del rango |
| `endIsoDate` | ISO 8601 | Fin del rango |

**Respuesta:**
```json
[
  {
    "id": "uuid",
    "subBasinId": 1,
    "forecastId": "uuid",
    "dateTime": "2026-04-10T18:00:00Z",
    "latitude": 16.848,
    "longitude": -93.535,
    "rain": 2.50
  }
]
```

---

## Mapeo de Servicios Spring Boot → CloudStation

| Servicio Spring Boot | Base URL Original | CloudStation URL |
|---------------------|-------------------|------------------|
| `scg-ws` (core-service) | `lb://scg-ws` | `https://hidrometria.mx` |
| `automatic-stations-connector` | `lb://automatic-stations-connector` | `https://hidrometria.mx` |
| `rain-forecast-service` | `lb://rain-forecast` | `https://hidrometria.mx` |
| `hydro-model-service` | `lb://hydro-model` | `https://hidrometria.mx` |

**Para migrar**: cambiar solo la base URL en la configuración del cliente.

---
