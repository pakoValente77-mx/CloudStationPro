# Manual de Consumo de APIs — CloudStation PIH
## Documento para CENACE

| Campo | Valor |
|---|---|
| **Servidor** | `https://cloudstation.cfe.mx` (o IP/puerto asignado) |
| **Autenticación** | JWT Bearer Token ó Header `X-Api-Key` |
| **API Key** | `***REDACTED-API-KEY***` |
| **Usuario JWT** | `cenace` |
| **Contraseña** | `Cfe900##` |
| **Rol** | `ApiConsumer` |
| **Formato** | JSON (UTF-8) |

---

## 1. Autenticación

### Método 1: API Key (más simple)

Enviar el header `X-Api-Key` en cada solicitud:

```
X-Api-Key: ***REDACTED-API-KEY***
```

**Ejemplo cURL:**
```bash
curl -H "X-Api-Key: ***REDACTED-API-KEY***" \
  https://cloudstation.cfe.mx/api/get/station/all
```

### Método 2: JWT Bearer Token

**Paso 1 — Obtener token:**

```
POST /api/auth/login
Content-Type: application/json

{
  "username": "cenace",
  "password": "Cfe900##"
}
```

**Respuesta:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "expira": "2026-04-17T12:00:00Z",
  "usuario": "cenace",
  "nombre": "CENACE",
  "roles": ["ApiConsumer"]
}
```

**Paso 2 — Usar token en solicitudes:**
```
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
```

> **Nota:** El token JWT expira en 24 horas. Después de expirar, solicite uno nuevo con `/api/auth/login`.

---

## 2. Catálogo de Estaciones

### 2.1 Todas las estaciones
```
GET /api/get/station/all
```

**Respuesta:**
```json
[
  {
    "id": 1,
    "vendorId": "ANG-E-C",
    "centralId": 1,
    "code": "ANG-E-C",
    "assignedId": "ANG-01",
    "name": "Angostura Convencional",
    "clazz": "C",
    "type": "E",
    "subBasinId": 1,
    "weighingInput": 0.15,
    "latitude": "16.848",
    "longitude": "-93.535"
  }
]
```

| Campo | Descripción |
|---|---|
| `id` | Identificador único de la estación |
| `clazz` | `C` = Convencional, `A` = Automática |
| `type` | `E` = Embalse |
| `centralId` | ID de la central hidroeléctrica asociada |
| `assignedId` | Código asignado (ej. ANG-01) |

### 2.2 Solo estaciones convencionales
```
GET /api/get/station/conventional/all
```

### 2.3 Solo estaciones automáticas
```
GET /api/get/station/automatic/all
```

### 2.4 Estación por ID
```
GET /api/get/station/by/id/{stationId}
```

**Parámetro:**
| Parámetro | Tipo | Descripción |
|---|---|---|
| `stationId` | int | ID de la estación (1-10) |

**Ejemplo:**
```
GET /api/get/station/by/id/1
```

---

## 3. Reporte de Estación (Datos Horarios FunVasos)

### 3.1 Registros de un día completo
```
GET /api/get/station-report/records/by/station-id/{stationId}/date/{date}
```

| Parámetro | Tipo | Ejemplo | Descripción |
|---|---|---|---|
| `stationId` | int | `1` | ID de la estación |
| `date` | string | `2026-04-15` | Fecha en formato `yyyy-MM-dd` |

**Ejemplo:**
```
GET /api/get/station-report/records/by/station-id/1/date/2026-04-15
```

**Respuesta:**
```json
[
  {
    "id": 1,
    "hour": 0,
    "elevation": 536.12,
    "scale": 9845.30,
    "powerGeneration": 450.00,
    "spent": 280.50,
    "turbineSpent": 280.50,
    "input": 195.00,
    "unitsWorking": 4
  },
  {
    "id": 1,
    "hour": 1,
    "elevation": 536.10,
    "...": "..."
  }
]
```

| Campo | Unidad | Descripción |
|---|---|---|
| `hour` | - | Hora del día (0-23) |
| `elevation` | msnm | Nivel del embalse |
| `scale` | Mm³ | Almacenamiento |
| `powerGeneration` | GWh | Generación eléctrica |
| `spent` | m³/s | Extracción total |
| `turbineSpent` | m³/s | Extracción por turbinas |
| `input` | m³/s | Aportaciones (gasto) |
| `unitsWorking` | - | Número de unidades generando |

---

## 4. Catálogo de Presas

### 4.1 Todas las presas
```
GET /api/get/dam/all
```

**Respuesta:**
```json
[
  {
    "id": 1,
    "centralId": 1,
    "code": "ANG",
    "description": "Angostura",
    "nameValue": 542.10,
    "namoValue": 539.00,
    "naminoValue": 510,
    "usefulVolume": 11115.0,
    "offVolume": 6554.0,
    "totalVolume": 17669.0,
    "inputArea": 22000.0,
    "hasPreviousDam": false,
    "huiFactor": 1.0,
    "modelType": "daily"
  }
]
```

| Campo | Unidad | Descripción |
|---|---|---|
| `nameValue` | msnm | Nivel de Aguas Máximas Extraordinarias (NAME) |
| `namoValue` | msnm | Nivel de Aguas Máximas de Operación (NAMO) |
| `naminoValue` | msnm | Nivel de Aguas Mínimas de Operación (NAMINO) |
| `usefulVolume` | Mm³ | Volumen útil |
| `offVolume` | Mm³ | Volumen muerto |
| `totalVolume` | Mm³ | Volumen total |
| `inputArea` | km² | Área de captación |

### Presas disponibles (Cascada Grijalva)

| ID | Código | Presa | Central ID |
|---|---|---|---|
| 1 | ANG | Angostura | 1 |
| 2 | CHI | Chicoasén | 2 |
| 3 | MAL | Malpaso | 3 |
| 4 | JGR | Tapón Juan Grijalva | 4 |
| 5 | PEN | Peñitas | 5 |

---

## 5. Comportamiento de Presa (Dam Behavior)

```
GET /api/get/dam-behavior/date/{date}/central-id/{centralId}
```

| Parámetro | Tipo | Ejemplo | Descripción |
|---|---|---|---|
| `date` | string | `2026-04-15` | Fecha `yyyy-MM-dd` |
| `centralId` | int | `1` | ID de la central (1-5) |

**Ejemplo:**
```
GET /api/get/dam-behavior/date/2026-04-15/central-id/3
```

**Respuesta:**
```json
[
  {
    "dateTime": "2026-04-15T00:00:00",
    "hour": 0,
    "elevation": 185.50,
    "utilCapacity": 7200.30,
    "diffCapacity": -12.50,
    "inputSpending": 650.00,
    "inputVolume": 56.16,
    "turbineSpending": 580.00,
    "turbineVolume": 50.11,
    "chuteSpending": 0.00,
    "chuteVolume": 0.00,
    "totalSpending": 580.00,
    "totalVolume": 50.11,
    "generation": 320.00,
    "unitsWorking": 5,
    "inputAverage": 620.00
  }
]
```

| Campo | Unidad | Descripción |
|---|---|---|
| `elevation` | msnm | Nivel del embalse |
| `utilCapacity` | Mm³ | Capacidad útil |
| `diffCapacity` | Mm³ | Diferencia de capacidad vs hora anterior |
| `inputSpending` | m³/s | Gasto de aportación |
| `inputVolume` | Mm³ | Volumen de aportación |
| `turbineSpending` | m³/s | Gasto por turbinas |
| `turbineVolume` | Mm³ | Volumen por turbinas |
| `chuteSpending` | m³/s | Gasto por vertedores |
| `chuteVolume` | Mm³ | Volumen por vertedores |
| `totalSpending` | m³/s | Gasto total de extracción |
| `totalVolume` | Mm³ | Volumen total de extracción |
| `generation` | GWh | Generación eléctrica |
| `unitsWorking` | - | Unidades generando |
| `inputAverage` | m³/s | Aportación promedio |

> **Ruta alternativa:** `GET /api/get/dam-behavior/central-id/{centralId}/date/{date}`

---

## 6. Sensores de Estación Automática

### 6.1 Listar sensores disponibles
```
GET /automatic-station/api/get/sensor/by/station-id/{stationId}
```

| Parámetro | Tipo | Descripción |
|---|---|---|
| `stationId` | int | ID de la estación automática |

**Ejemplo:**
```
GET /automatic-station/api/get/sensor/by/station-id/2
```

**Respuesta:**
```json
[
  {
    "sensorNumber": 1,
    "variable": "elevación",
    "assignedId": "ANG-02",
    "stationId": 2,
    "stationName": "Angostura Automática",
    "totalRecords": 15420
  },
  {
    "sensorNumber": 2,
    "variable": "precipitación",
    "assignedId": "ANG-02",
    "stationId": 2,
    "stationName": "Angostura Automática",
    "totalRecords": 15200
  }
]
```

### 6.2 Valor de un sensor específico
```
GET /automatic-station/api/get/sensor-value/by/assigned-id/{assignedId}/sensor-number/{sensorNumber}/date/{date}/hour/{hour}
```

| Parámetro | Tipo | Ejemplo | Descripción |
|---|---|---|---|
| `assignedId` | string | `ANG-02` | Código asignado de la estación |
| `sensorNumber` | int | `1` | Número del sensor (de `/sensor/by/station-id`) |
| `date` | string | `2026-04-15` | Fecha `yyyy-MM-dd` |
| `hour` | int | `12` | Hora UTC (0-23) |

**Ejemplo:**
```
GET /automatic-station/api/get/sensor-value/by/assigned-id/ANG-02/sensor-number/1/date/2026-04-15/hour/12
```

**Respuesta:**
```json
{
  "assignedId": "ANG-02",
  "sensorNumber": 1,
  "variable": "elevación",
  "dateTime": "2026-04-15T12:00:00",
  "value": 536.12,
  "accumulated": null,
  "sum": null,
  "max": 536.50,
  "min": 535.80,
  "average": 536.12
}
```

---

## 7. Referencia Rápida de Estaciones

| ID | Código | Nombre | Clase | Central |
|---|---|---|---|---|
| 1 | ANG-01 | Angostura Convencional | C | 1-Angostura |
| 2 | ANG-02 | Angostura Automática | A | 1-Angostura |
| 3 | CHI-01 | Chicoasén Convencional | C | 2-Chicoasén |
| 4 | CHI-02 | Chicoasén Automática | A | 2-Chicoasén |
| 5 | MAL-01 | Malpaso Convencional | C | 3-Malpaso |
| 6 | MAL-02 | Malpaso Automática | A | 3-Malpaso |
| 7 | JGR-01 | Juan Grijalva Convencional | C | 4-Juan Grijalva |
| 8 | JGR-02 | Juan Grijalva Automática | A | 4-Juan Grijalva |
| 9 | PEN-01 | Peñitas Convencional | C | 5-Peñitas |
| 10 | PEN-02 | Peñitas Automática | A | 5-Peñitas |

---

## 8. Códigos de Respuesta HTTP

| Código | Significado |
|---|---|
| `200` | Éxito |
| `400` | Parámetros inválidos (fecha mal formateada, etc.) |
| `401` | No autorizado (API Key o token inválido/expirado) |
| `404` | Recurso no encontrado (estación, presa o datos del día) |
| `500` | Error interno del servidor |

---

## 9. Resumen de Endpoints

| # | Método | Endpoint | Descripción |
|---|---|---|---|
| 1 | POST | `/api/auth/login` | Obtener JWT token |
| 2 | GET | `/api/get/station/all` | Todas las estaciones |
| 3 | GET | `/api/get/station/conventional/all` | Estaciones convencionales |
| 4 | GET | `/api/get/station/automatic/all` | Estaciones automáticas |
| 5 | GET | `/api/get/station/by/id/{stationId}` | Estación por ID |
| 6 | GET | `/api/get/station-report/records/by/station-id/{stationId}/date/{date}` | Datos horarios de estación |
| 7 | GET | `/api/get/dam/all` | Todas las presas |
| 8 | GET | `/api/get/dam-behavior/date/{date}/central-id/{centralId}` | Comportamiento de presa |
| 9 | GET | `/automatic-station/api/get/sensor/by/station-id/{stationId}` | Sensores de estación automática |
| 10 | GET | `/automatic-station/api/get/sensor-value/by/assigned-id/{assignedId}/sensor-number/{sensorNumber}/date/{date}/hour/{hour}` | Valor de sensor |

---

*Documento generado: Abril 2026 — CloudStation PIH — Subgerencia de Programación y Control de Generación Grijalva*
