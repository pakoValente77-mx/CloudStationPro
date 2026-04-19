"""
CloudStation PIH — Cliente de consulta de APIs para CENACE
===========================================================
Uso:
    python api_client_cenace.py

Requisitos:
    pip install requests tabulate
"""

import requests
from datetime import datetime, timedelta
from tabulate import tabulate

# =====================================================================
# CONFIGURACIÓN — Ajustar URL del servidor
# =====================================================================
BASE_URL   = "https://cloudstation.cfe.mx"   # Cambiar a IP:puerto si aplica
API_KEY    = "***REDACTED-API-KEY***"
USERNAME   = "cenace"
PASSWORD   = "Cfe900##"

# =====================================================================
# Sesión HTTP reutilizable
# =====================================================================
session = requests.Session()
session.headers.update({
    "X-Api-Key": API_KEY,
    "Accept": "application/json",
})
session.verify = True  # Cambiar a False si es HTTPS con certificado auto-firmado


def login_jwt():
    """Autenticación por JWT (alternativa al API Key)."""
    r = session.post(f"{BASE_URL}/api/auth/login", json={
        "username": USERNAME,
        "password": PASSWORD,
    })
    r.raise_for_status()
    data = r.json()
    token = data["token"]
    print(f"✓ Login exitoso. Token expira: {data['expira']}")
    print(f"  Usuario: {data['usuario']}  Roles: {data['roles']}")
    # Usar JWT en lugar de API Key
    session.headers.update({"Authorization": f"Bearer {token}"})
    session.headers.pop("X-Api-Key", None)
    return token


# =====================================================================
# 1. ESTACIONES
# =====================================================================

def get_all_stations():
    """Obtener todas las estaciones."""
    r = session.get(f"{BASE_URL}/api/get/station/all")
    r.raise_for_status()
    stations = r.json()
    print(f"\n{'='*70}")
    print(f"  TODAS LAS ESTACIONES ({len(stations)})")
    print(f"{'='*70}")
    table = [[s["id"], s["assignedId"], s["name"], s["clazz"], s["centralId"]]
             for s in stations]
    print(tabulate(table, headers=["ID", "Código", "Nombre", "Clase", "Central"],
                   tablefmt="grid"))
    return stations


def get_conventional_stations():
    """Obtener estaciones convencionales."""
    r = session.get(f"{BASE_URL}/api/get/station/conventional/all")
    r.raise_for_status()
    stations = r.json()
    print(f"\n  ESTACIONES CONVENCIONALES ({len(stations)})")
    for s in stations:
        print(f"    [{s['id']}] {s['assignedId']} — {s['name']}")
    return stations


def get_automatic_stations():
    """Obtener estaciones automáticas."""
    r = session.get(f"{BASE_URL}/api/get/station/automatic/all")
    r.raise_for_status()
    stations = r.json()
    print(f"\n  ESTACIONES AUTOMÁTICAS ({len(stations)})")
    for s in stations:
        print(f"    [{s['id']}] {s['assignedId']} — {s['name']}")
    return stations


def get_station_by_id(station_id):
    """Obtener una estación por ID."""
    r = session.get(f"{BASE_URL}/api/get/station/by/id/{station_id}")
    r.raise_for_status()
    s = r.json()
    print(f"\n  ESTACIÓN {station_id}")
    print(f"    Nombre:    {s['name']}")
    print(f"    Código:    {s['assignedId']}")
    print(f"    Clase:     {'Convencional' if s['clazz'] == 'C' else 'Automática'}")
    print(f"    Central:   {s['centralId']}")
    print(f"    Subcuenca: {s['subBasinId']}")
    print(f"    Lat/Lon:   {s['latitude']}, {s['longitude']}")
    return s


# =====================================================================
# 2. REPORTE DE ESTACIÓN (DATOS HORARIOS)
# =====================================================================

def get_station_report(station_id, date_str):
    """Obtener datos horarios de una estación para un día."""
    r = session.get(
        f"{BASE_URL}/api/get/station-report/records/by/station-id/{station_id}/date/{date_str}")
    r.raise_for_status()
    records = r.json()
    print(f"\n{'='*70}")
    print(f"  REPORTE ESTACIÓN {station_id} — {date_str} ({len(records)} horas)")
    print(f"{'='*70}")
    if records:
        table = [[
            r["hour"],
            f"{r['elevation']:.2f}" if r.get("elevation") else "-",
            f"{r['scale']:.2f}" if r.get("scale") else "-",
            f"{r['input']:.2f}" if r.get("input") else "-",
            f"{r['spent']:.2f}" if r.get("spent") else "-",
            f"{r['powerGeneration']:.2f}" if r.get("powerGeneration") else "-",
            r.get("unitsWorking", "-"),
        ] for r in records]
        print(tabulate(table,
                       headers=["Hora", "Elevación", "Almac.", "Aportación",
                                "Extracción", "Generación", "Unidades"],
                       tablefmt="grid", floatfmt=".2f"))
    else:
        print("    Sin datos para esta fecha")
    return records


# =====================================================================
# 3. PRESAS
# =====================================================================

def get_all_dams():
    """Obtener todas las presas."""
    r = session.get(f"{BASE_URL}/api/get/dam/all")
    r.raise_for_status()
    dams = r.json()
    print(f"\n{'='*70}")
    print(f"  PRESAS DE LA CASCADA GRIJALVA ({len(dams)})")
    print(f"{'='*70}")
    table = [[d["id"], d["code"], d["description"],
              f"{d['namoValue']:.1f}", f"{d['nameValue']:.1f}",
              f"{d['usefulVolume']:.0f}", f"{d['totalVolume']:.0f}"]
             for d in dams]
    print(tabulate(table,
                   headers=["ID", "Clave", "Presa", "NAMO", "NAME",
                            "Vol.Útil", "Vol.Total"],
                   tablefmt="grid"))
    return dams


# =====================================================================
# 4. COMPORTAMIENTO DE PRESA
# =====================================================================

def get_dam_behavior(date_str, central_id):
    """Obtener comportamiento horario de una presa."""
    r = session.get(
        f"{BASE_URL}/api/get/dam-behavior/date/{date_str}/central-id/{central_id}")
    r.raise_for_status()
    behaviors = r.json()
    central_names = {1: "Angostura", 2: "Chicoasén", 3: "Malpaso",
                     4: "Juan Grijalva", 5: "Peñitas"}
    name = central_names.get(central_id, f"Central {central_id}")
    print(f"\n{'='*70}")
    print(f"  COMPORTAMIENTO DE PRESA: {name} — {date_str} ({len(behaviors)} horas)")
    print(f"{'='*70}")
    if behaviors:
        table = [[
            b["hour"],
            f"{b['elevation']:.2f}" if b.get("elevation") else "-",
            f"{b['inputSpending']:.1f}" if b.get("inputSpending") else "-",
            f"{b['turbineSpending']:.1f}" if b.get("turbineSpending") else "-",
            f"{b['chuteSpending']:.1f}" if b.get("chuteSpending") else "-",
            f"{b['totalSpending']:.1f}" if b.get("totalSpending") else "-",
            f"{b['generation']:.2f}" if b.get("generation") else "-",
            b.get("unitsWorking", "-"),
        ] for b in behaviors]
        print(tabulate(table,
                       headers=["Hora", "Elevación", "Aport.Q", "Turb.Q",
                                "Vert.Q", "Total.Q", "Generación", "Uds"],
                       tablefmt="grid"))
    else:
        print("    Sin datos para esta fecha")
    return behaviors


# =====================================================================
# 5. SENSORES DE ESTACIÓN AUTOMÁTICA
# =====================================================================

def get_sensors(station_id):
    """Listar sensores disponibles para una estación automática."""
    r = session.get(
        f"{BASE_URL}/automatic-station/api/get/sensor/by/station-id/{station_id}")
    r.raise_for_status()
    sensors = r.json()
    print(f"\n  SENSORES DE ESTACIÓN {station_id} ({len(sensors)} variables)")
    for s in sensors:
        print(f"    Sensor #{s['sensorNumber']}: {s['variable']} "
              f"({s['totalRecords']} registros) — {s['assignedId']}")
    return sensors


def get_sensor_value(assigned_id, sensor_number, date_str, hour):
    """Obtener valor de un sensor en fecha/hora específica."""
    r = session.get(
        f"{BASE_URL}/automatic-station/api/get/sensor-value/"
        f"by/assigned-id/{assigned_id}/sensor-number/{sensor_number}/"
        f"date/{date_str}/hour/{hour}")
    r.raise_for_status()
    v = r.json()
    print(f"\n  VALOR SENSOR #{sensor_number} — {assigned_id}")
    print(f"    Variable:  {v['variable']}")
    print(f"    Fecha/Hora:{v['dateTime']}")
    print(f"    Valor:     {v['value']}")
    if v.get("max") is not None:
        print(f"    Máximo:    {v['max']}")
    if v.get("min") is not None:
        print(f"    Mínimo:    {v['min']}")
    if v.get("average") is not None:
        print(f"    Promedio:  {v['average']}")
    return v


# =====================================================================
# DEMO: Ejecución completa de ejemplo
# =====================================================================

def main():
    print("=" * 70)
    print("  CloudStation PIH — Cliente de consulta API para CENACE")
    print("=" * 70)
    print(f"  Servidor: {BASE_URL}")
    print(f"  Fecha:    {datetime.now().strftime('%Y-%m-%d %H:%M')}")
    print()

    # Usar ayer como fecha de ejemplo
    yesterday = (datetime.now() - timedelta(days=1)).strftime("%Y-%m-%d")

    # --- 1. Catálogo de estaciones ---
    get_all_stations()
    get_conventional_stations()
    get_automatic_stations()
    get_station_by_id(1)

    # --- 2. Reporte horario de Angostura ---
    get_station_report(station_id=1, date_str=yesterday)

    # --- 3. Catálogo de presas ---
    get_all_dams()

    # --- 4. Comportamiento de Malpaso ---
    get_dam_behavior(date_str=yesterday, central_id=3)

    # --- 5. Sensores de Angostura Automática ---
    sensors = get_sensors(station_id=2)
    if sensors:
        # Consultar el primer sensor a las 12:00 UTC
        get_sensor_value(
            assigned_id=sensors[0]["assignedId"],
            sensor_number=sensors[0]["sensorNumber"],
            date_str=yesterday,
            hour=12
        )

    print(f"\n{'='*70}")
    print("  Consulta completada exitosamente")
    print(f"{'='*70}")


if __name__ == "__main__":
    main()
