# -*- coding: utf-8 -*-
"""
Genera el manual PDF de la API REST CloudStation para CENACE.
Usa la IP demostrativa 10.222.35.66 en los ejemplos.
"""

from fpdf import FPDF
import json, textwrap

BASE = "http://10.222.35.66"
API_KEY = "***REDACTED-API-KEY***"

class ManualPDF(FPDF):
    def __init__(self):
        super().__init__()
        fontdir = r"C:\Windows\Fonts"
        self.add_font("Segoe", "", f"{fontdir}\\segoeui.ttf")
        self.add_font("Segoe", "B", f"{fontdir}\\segoeuib.ttf")
        self.add_font("Segoe", "I", f"{fontdir}\\segoeuii.ttf")
        self.add_font("Consolas", "", f"{fontdir}\\consola.ttf")

    def header(self):
        self.set_font("Segoe", "B", 10)
        self.set_text_color(100, 100, 100)
        self.cell(0, 6, "PIH API — Manual de Endpoints para CENACE", align="C", new_x="LMARGIN", new_y="NEXT")
        self.line(10, self.get_y(), 200, self.get_y())
        self.ln(3)

    def footer(self):
        self.set_y(-15)
        self.set_font("Segoe", "I", 8)
        self.set_text_color(128, 128, 128)
        self.cell(0, 10, f"Página {self.page_no()}/{{nb}}", align="C")

    def titulo_seccion(self, texto):
        self.set_font("Segoe", "B", 14)
        self.set_text_color(0, 51, 102)
        self.ln(4)
        self.cell(0, 10, texto, new_x="LMARGIN", new_y="NEXT")
        self.set_draw_color(0, 51, 102)
        self.line(10, self.get_y(), 200, self.get_y())
        self.ln(3)

    def titulo_endpoint(self, metodo, ruta):
        self.set_font("Segoe", "B", 11)
        self.set_text_color(0, 102, 0)
        self.cell(0, 8, f"{metodo}  {ruta}", new_x="LMARGIN", new_y="NEXT")
        self.ln(1)

    def parrafo(self, txt):
        self.set_font("Segoe", "", 10)
        self.set_text_color(0, 0, 0)
        self.multi_cell(0, 5, txt)
        self.ln(1)

    def label(self, txt):
        self.set_font("Segoe", "B", 10)
        self.set_text_color(60, 60, 60)
        self.cell(0, 6, txt, new_x="LMARGIN", new_y="NEXT")

    def code_block(self, txt):
        self.set_font("Consolas", "", 8)
        self.set_fill_color(240, 240, 240)
        self.set_text_color(30, 30, 30)
        # wrap long lines
        lines = []
        for l in txt.split("\n"):
            if len(l) > 105:
                for chunk in textwrap.wrap(l, 105, break_long_words=True, break_on_hyphens=False):
                    lines.append(chunk)
            else:
                lines.append(l)
        for line in lines:
            self.cell(0, 4.5, "  " + line, fill=True, new_x="LMARGIN", new_y="NEXT")
        self.ln(2)

    def tabla_params(self, params):
        """params = list of (nombre, tipo, descripcion)"""
        self.set_font("Segoe", "B", 9)
        self.set_fill_color(0, 51, 102)
        self.set_text_color(255, 255, 255)
        self.cell(40, 6, "Parámetro", border=1, fill=True)
        self.cell(25, 6, "Tipo", border=1, fill=True)
        self.cell(0, 6, "Descripción", border=1, fill=True, new_x="LMARGIN", new_y="NEXT")
        self.set_font("Segoe", "", 9)
        self.set_text_color(0, 0, 0)
        fill = False
        for nombre, tipo, desc in params:
            if fill:
                self.set_fill_color(245, 245, 245)
            else:
                self.set_fill_color(255, 255, 255)
            self.cell(40, 5.5, nombre, border=1, fill=True)
            self.cell(25, 5.5, tipo, border=1, fill=True)
            self.cell(0, 5.5, desc, border=1, fill=True, new_x="LMARGIN", new_y="NEXT")
            fill = not fill
        self.ln(2)

    def check_space(self, h=60):
        if self.get_y() > 297 - h:
            self.add_page()


def build_curl(method, path, body=None):
    hdr = f'-H "X-Api-Key: {API_KEY}"'
    url = f"{BASE}{path}"
    if body:
        return f'curl -X {method} {hdr} -H "Content-Type: application/json" \\\n     -d \'{json.dumps(body, ensure_ascii=False)}\' \\\n     "{url}"'
    return f'curl -X {method} {hdr} "{url}"'


def main():
    pdf = ManualPDF()
    pdf.alias_nb_pages()
    pdf.set_auto_page_break(auto=True, margin=20)
    pdf.add_page()

    # ==================== PORTADA ====================
    pdf.ln(40)
    pdf.set_font("Segoe", "B", 32)
    pdf.set_text_color(0, 51, 102)
    pdf.cell(0, 16, "PIH", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.set_font("Segoe", "B", 18)
    pdf.cell(0, 12, "Plataforma Integral Hidrometeorológica", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.ln(6)
    pdf.set_font("Segoe", "", 16)
    pdf.set_text_color(80, 80, 80)
    pdf.cell(0, 10, "Manual de Endpoints API para CENACE", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.ln(8)
    pdf.set_font("Segoe", "", 12)
    pdf.set_text_color(100, 100, 100)
    pdf.cell(0, 8, "Sistema de Pronóstico e Información Hidrológica", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.cell(0, 8, "Cuenca del Río Grijalva", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.ln(20)
    pdf.set_font("Segoe", "I", 11)
    pdf.set_text_color(120, 120, 120)
    pdf.cell(0, 8, f"URL Base: {BASE}", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.cell(0, 8, "Abril 2026", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.ln(30)
    pdf.set_font("Segoe", "", 10)
    pdf.set_text_color(0, 0, 0)
    pdf.multi_cell(0, 6, (
        "Este documento describe cada uno de los endpoints REST disponibles en la API de PIH, "
        "incluyendo la ruta, parámetros, ejemplo de consulta con curl, y la respuesta esperada.\n\n"
        "Autenticación: Incluir el header X-Api-Key con valor proporcionado, "
        "o un token JWT Bearer con rol ApiConsumer."
    ), align="C")

    # ==================== AUTENTICACIÓN ====================
    pdf.add_page()
    pdf.titulo_seccion("1. Autenticación")
    pdf.parrafo(
        "Todos los endpoints requieren autenticación mediante uno de los siguientes métodos:"
    )
    pdf.label("Opción A — API Key (recomendado):")
    pdf.code_block(f'X-Api-Key: {API_KEY}')
    pdf.label("Opción B — JWT Bearer Token:")
    pdf.parrafo(
        "Solicitar un token JWT al endpoint POST /api/auth/login enviando usuario y contraseña.\n"
        "El token debe pertenecer a un usuario con rol ApiConsumer, SuperAdmin o Administrador."
    )
    pdf.label("Ejemplo — Obtener token JWT:")
    pdf.code_block(
        f'curl -X POST -H "Content-Type: application/json" \\\n'
        f'     -d \'{{"username": "cenace", "password": "Cfe900##"}}\' \\\n'
        f'     "{BASE}/api/auth/login"'
    )
    pdf.label("Respuesta:")
    pdf.code_block(json.dumps({
        "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
        "expira": "2026-04-17T18:00:00Z",
        "usuario": "cenace",
        "nombre": "CENACE Consulta",
        "roles": ["ApiConsumer"]
    }, indent=2, ensure_ascii=False))
    pdf.label("Usar el token en solicitudes posteriores:")
    pdf.code_block(
        f'curl -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIs..." \\\n'
        f'     "{BASE}/api/get/station/all"'
    )
    pdf.ln(3)
    pdf.parrafo(
        'En los ejemplos del resto del documento se usa el header X-Api-Key por simplicidad.'
    )

    # ==================== ESTACIONES ====================
    pdf.add_page()
    pdf.titulo_seccion("2. Estaciones")

    # 2.1 station/all
    pdf.titulo_endpoint("GET", "/api/get/station/all")
    pdf.parrafo("Retorna todas las estaciones activas de la red de monitoreo (automáticas y convencionales).")
    pdf.label("Ejemplo de consulta:")
    pdf.code_block(build_curl("GET", "/api/get/station/all"))
    pdf.label("Respuesta (extracto — total: 221 estaciones):")
    pdf.code_block(json.dumps([
        {"id":"CFE0000003","databaseId":"779023c1-d0bd-4480-ae29-aefa86180ed6","name":"Acala",
         "latitude":16.6606,"longitude":-92.9535,"dcpId":"E890F8B4",
         "organismo":"Comisión Federal de Electricidad","label":"ACL",
         "isDam":False,"goes":True,"gprs":False,"radio":False,"type":"automatic"},
        {"id":"...","name":"...","note":"... 220 estaciones más"}
    ], indent=2, ensure_ascii=False))

    # 2.2 station/automatic/all
    pdf.check_space(50)
    pdf.titulo_endpoint("GET", "/api/get/station/automatic/all")
    pdf.parrafo("Retorna solo las estaciones automáticas (con telemetría GOES). Total: 207 estaciones.")
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/station/automatic/all"))
    pdf.parrafo("La respuesta tiene el mismo formato que station/all, filtrado por goes=true.")

    # 2.3 station/conventional/all
    pdf.check_space(50)
    pdf.titulo_endpoint("GET", "/api/get/station/conventional/all")
    pdf.parrafo("Retorna las estaciones convencionales cargadas desde el Excel BHG.")
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/station/conventional/all"))

    # 2.4 station/by/id
    pdf.check_space(70)
    pdf.titulo_endpoint("GET", "/api/get/station/by/id/{stationId}")
    pdf.parrafo("Retorna una estación específica por su IdAsignado.")
    pdf.tabla_params([("stationId", "string", "ID asignado de la estación (ej: CFE0000003)")])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/station/by/id/CFE0000003"))
    pdf.label("Respuesta:")
    pdf.code_block(json.dumps(
        {"id":"CFE0000003","databaseId":"779023c1-d0bd-4480-ae29-aefa86180ed6","name":"Acala",
         "latitude":16.6606,"longitude":-92.9535,"dcpId":"E890F8B4",
         "organismo":"Comisión Federal de Electricidad","label":"ACL",
         "isDam":False,"goes":True,"gprs":False,"radio":False,"type":"automatic"},
        indent=2, ensure_ascii=False
    ))

    # 2.5 station/by/central-id/class/type
    pdf.check_space(70)
    pdf.titulo_endpoint("GET", "/api/get/station/by/central-id/{centralId}/class/{clazz}/type/{type}")
    pdf.parrafo(
        "Busca estación de presa por ID de central hidroeléctrica, clase y tipo.\n"
        "Clase: A=Automática, C=Convencional. Tipo: E=Embalse, H=Hidrométrica."
    )
    pdf.tabla_params([
        ("centralId", "int", "ID de la central (1-5)"),
        ("clazz", "char", "Clase: A (automática) o C (convencional)"),
        ("type", "char", "Tipo: E (embalse) o H (hidrométrica)"),
    ])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/station/by/central-id/1/class/A/type/E"))

    # 2.6 station/hydro-model/by/sub-basin
    pdf.check_space(60)
    pdf.titulo_endpoint("GET", "/api/get/station/hydro-model/by/sub-basin/{subBasinId}")
    pdf.parrafo("Retorna todas las estaciones usadas por el modelo hidrológico de una subcuenca.")
    pdf.tabla_params([("subBasinId", "int", "ID de subcuenca (1-5)")])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/station/hydro-model/by/sub-basin/1"))

    # ==================== CENTRALES ====================
    pdf.add_page()
    pdf.titulo_seccion("3. Centrales Hidroeléctricas")

    pdf.titulo_endpoint("GET", "/api/get/central/by/id/{id}")
    pdf.parrafo("Retorna los datos de una central hidroeléctrica del sistema Grijalva.")
    pdf.tabla_params([("id", "int", "ID de la central (1=Angostura, 2=Chicoasén, 3=Malpaso, 4=JGrijalva, 5=Peñitas)")])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/central/by/id/1"))
    pdf.label("Respuesta:")
    pdf.code_block(json.dumps(
        {"id":1,"previousCentralId":None,"idCuenca":1,"idSubcuenca":1,
         "clave20":"ANG","claveCenace":"K02","claveSap":"ANG",
         "nombre":"C.H. Angostura","unidades":5,"capacidadInstalada":900,
         "consumoEspecifico":4.1,"latitud":16.848,"longitud":-93.535,"orden":1},
        indent=2, ensure_ascii=False
    ))

    # ==================== PRESAS ====================
    pdf.add_page()
    pdf.titulo_seccion("4. Presas")

    # dam/all
    pdf.titulo_endpoint("GET", "/api/get/dam/all")
    pdf.parrafo("Retorna todas las presas del sistema Grijalva (5 presas).")
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/dam/all"))
    pdf.label("Respuesta (extracto):")
    pdf.code_block(json.dumps([
        {"id":1,"centralId":1,"code":"ANG","description":"Angostura",
         "nameValue":542.1,"namoValue":539,"naminoValue":510,
         "usefulVolume":11115,"offVolume":6554,"totalVolume":17669,
         "inputArea":22000,"hasPreviousDam":False,"huiFactor":1,"modelType":"daily"},
        {"id":2,"centralId":2,"code":"CHI","description":"Chicoasén","note":"..."},
    ], indent=2, ensure_ascii=False))

    # dam/by/id
    pdf.check_space(60)
    pdf.titulo_endpoint("GET", "/api/get/dam/by/id/{damId}")
    pdf.parrafo("Retorna una presa por su ID.")
    pdf.tabla_params([("damId", "int", "ID de la presa (1-5)")])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/dam/by/id/1"))
    pdf.label("Respuesta:")
    pdf.code_block(json.dumps(
        {"id":1,"centralId":1,"code":"ANG","description":"Angostura",
         "nameValue":542.1,"namoValue":539,"naminoValue":510,
         "usefulVolume":11115,"offVolume":6554,"totalVolume":17669,
         "inputArea":22000,"hasPreviousDam":False,"huiFactor":1,"modelType":"daily"},
        indent=2, ensure_ascii=False
    ))

    # dam/by/central
    pdf.check_space(50)
    pdf.titulo_endpoint("GET", "/api/get/dam/by/central/{centralId}")
    pdf.parrafo("Retorna la presa asociada a una central hidroeléctrica.")
    pdf.tabla_params([("centralId", "int", "ID de la central (1-5)")])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/dam/by/central/1"))
    pdf.parrafo("Misma respuesta que dam/by/id cuando el centralId coincide.")

    # ==================== SUBCUENCAS ====================
    pdf.add_page()
    pdf.titulo_seccion("5. Subcuencas")

    pdf.titulo_endpoint("GET", "/api/get/sub-basin/by/id/{id}")
    pdf.parrafo(
        "Retorna una subcuenca con sus coeficientes HUI (Hidrograma Unitario Instantáneo), "
        "factor de entrada y tiempo de transferencia."
    )
    pdf.tabla_params([("id", "int", "ID de subcuenca (1=Angostura, 2=Medio Mezcalapa, 3=Malpaso, 4=JGrijalva, 5=Peñitas)")])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/sub-basin/by/id/1"))
    pdf.label("Respuesta:")
    pdf.code_block(json.dumps(
        {"id":1,"idCuenca":1,"clave":"ANG","nombre":"Angostura",
         "inputFactor":0.15,"transferTime":0,
         "hoursRead":[6,12,18,24],
         "hui":[0.05,0.1,0.2,0.25,0.2,0.1,0.05,0.03,0.02],
         "previousDaysNumber":None},
        indent=2, ensure_ascii=False
    ))

    # ==================== ELEVACIÓN-CAPACIDAD ====================
    pdf.add_page()
    pdf.titulo_seccion("6. Curvas Elevación-Capacidad")

    pdf.titulo_endpoint("GET", "/api/get/elevation-capacity/by/central/{centralId}/elevation/{elevation}")
    pdf.parrafo(
        "Interpola la capacidad de almacenamiento (Mm³) a partir de una elevación (msnm) "
        "usando la curva elevación-capacidad de la presa."
    )
    pdf.tabla_params([
        ("centralId", "int", "ID de la central (1-5)"),
        ("elevation", "float", "Elevación en metros sobre nivel del mar"),
    ])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/elevation-capacity/by/central/1/elevation/520"))
    pdf.label("Respuesta:")
    pdf.code_block(json.dumps(
        {"id":0,"centralId":1,"elevation":520,"capacity":6392.5,
         "capacityArea":442.87,"specificSpend":4.1131},
        indent=2, ensure_ascii=False
    ))

    pdf.check_space(60)
    pdf.titulo_endpoint("GET", "/api/get/elevation-capacity/by/central/{centralId}/capacity/{capacity}")
    pdf.parrafo(
        "Interpolación inversa: obtiene la elevación a partir de una capacidad de almacenamiento."
    )
    pdf.tabla_params([
        ("centralId", "int", "ID de la central (1-5)"),
        ("capacity", "double", "Capacidad en Mm³"),
    ])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/elevation-capacity/by/central/1/capacity/6392.5"))
    pdf.label("Respuesta:")
    pdf.code_block(json.dumps(
        {"id":0,"centralId":1,"elevation":520.0,"capacity":6392.5,
         "capacityArea":None,"specificSpend":None},
        indent=2, ensure_ascii=False
    ))

    # ==================== COMPORTAMIENTO DE PRESA ====================
    pdf.add_page()
    pdf.titulo_seccion("7. Comportamiento de Presa (Dam Behavior)")

    pdf.titulo_endpoint("GET", "/api/get/dam-behavior/central-id/{centralId}/date/{date}")
    pdf.parrafo(
        "Retorna el comportamiento horario de una presa para un día específico. "
        "Incluye elevación, almacenamiento, aportaciones, extracciones, generación y unidades en operación."
    )
    pdf.tabla_params([
        ("centralId", "int", "ID de la central (1-5)"),
        ("date", "string", "Fecha en formato yyyy-MM-dd"),
    ])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/dam-behavior/central-id/1/date/2026-03-24"))
    pdf.label("Respuesta (extracto — 10 registros horarios):")
    pdf.code_block(json.dumps([
        {"dateTime":"2026-03-24T06:00:00","hour":1,"elevation":525.25,
         "utilCapacity":8876.19,"specificSpend":None,"diffCapacity":0,
         "inputSpending":287.6262,"inputVolume":1.035454,
         "turbineSpending":287.6262,"turbineVolume":1.035454,
         "chuteSpending":None,"chuteVolume":None,
         "totalSpending":287.6262,"totalVolume":1.035454,
         "generation":265.166,"unitsWorking":2,"inputAverage":None},
        {"note":"... más registros horarios"}
    ], indent=2, ensure_ascii=False))

    # dam-behavior alternate route
    pdf.check_space(50)
    pdf.titulo_endpoint("GET", "/api/get/dam-behavior/date/{date}/central-id/{centralId}")
    pdf.parrafo("Ruta alternativa con los parámetros en orden inverso. Misma respuesta.")
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/dam-behavior/date/2026-03-24/central-id/1"))

    # primary-flow-spending
    pdf.check_space(60)
    pdf.titulo_endpoint("GET", "/api/get/dam-behavior/primary-flow-spending/by/central-id/{centralId}/date/{date}/hour/{hour}")
    pdf.parrafo(
        "Retorna el gasto total de extracción (m³/s) de una presa a una hora específica."
    )
    pdf.tabla_params([
        ("centralId", "int", "ID de la central (1-5)"),
        ("date", "string", "Fecha yyyy-MM-dd"),
        ("hour", "int", "Hora (1-24)"),
    ])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/dam-behavior/primary-flow-spending/by/central-id/1/date/2026-03-24/hour/1"))
    pdf.label("Respuesta:")
    pdf.code_block("287.6262")

    # ==================== REPORTE DE ESTACIÓN ====================
    pdf.add_page()
    pdf.titulo_seccion("8. Reporte de Estación (Station Report)")

    pdf.titulo_endpoint("GET", "/api/get/station-report/records/by/station-id/{stationId}/date/{date}")
    pdf.parrafo(
        "Retorna todos los registros horarios de un día para una estación de presa. "
        "stationId corresponde al centralId (1-5)."
    )
    pdf.tabla_params([
        ("stationId", "string", "ID de central (1-5)"),
        ("date", "string", "Fecha yyyy-MM-dd"),
    ])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/station-report/records/by/station-id/1/date/2026-03-24"))
    pdf.label("Respuesta (extracto):")
    pdf.code_block(json.dumps([
        {"id":"1","hour":1,"elevation":525.25,"scale":8876.19,
         "powerGeneration":265.166,"spent":287.6262,"turbineSpent":287.6262,
         "input":287.6262,"unitsWorking":2},
        {"note":"... más registros horarios"}
    ], indent=2, ensure_ascii=False))

    pdf.check_space(60)
    pdf.titulo_endpoint("GET", "/api/get/station-report/records/by/station/{stationId}/date/{date}/hour/{hour}")
    pdf.parrafo("Retorna el registro de una hora específica.")
    pdf.tabla_params([
        ("stationId", "int", "ID de central (1-5)"),
        ("date", "string", "Fecha yyyy-MM-dd"),
        ("hour", "int", "Hora (1-24)"),
    ])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/station-report/records/by/station/1/date/2026-03-24/hour/1"))
    pdf.label("Respuesta:")
    pdf.code_block(json.dumps(
        {"id":1,"hour":1,"elevation":525.25,"scale":8876.19,
         "powerGeneration":265.166,"spent":287.6262,
         "precipitation":None,"unitsWorking":2},
        indent=2, ensure_ascii=False
    ))

    # ==================== SENSORES ====================
    pdf.add_page()
    pdf.titulo_seccion("9. Sensores de Estaciones Automáticas")

    pdf.titulo_endpoint("GET", "/automatic-station/api/get/sensor/by/station-id/{stationId}")
    pdf.parrafo(
        "Lista los sensores (variables) disponibles para una estación automática. "
        "Las variables están ordenadas alfabéticamente y el sensorNumber es su posición."
    )
    pdf.tabla_params([("stationId", "string", "ID asignado de la estación (ej: CFE0000012)")])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/automatic-station/api/get/sensor/by/station-id/CFE0000012"))
    pdf.label("Respuesta (extracto — 16 sensores):")
    pdf.code_block(json.dumps([
        {"sensorNumber":1,"variable":"dirección_de_ráfaga","assignedId":"CFE0000012",
         "dcpId":"E8908E24","stationId":"CFE0000012","stationName":"Puente Concordia","totalRecords":3081},
        {"sensorNumber":6,"variable":"precipitación","assignedId":"CFE0000012",
         "dcpId":"E8908E24","stationId":"CFE0000012","stationName":"Puente Concordia","totalRecords":3098},
        {"note":"... 14 sensores más"}
    ], indent=2, ensure_ascii=False))

    pdf.check_space(60)
    pdf.titulo_endpoint("GET", "/automatic-station/api/get/sensor-value/by/assigned-id/{assignedId}/sensor-number/{sensorNumber}/date/{date}/hour/{hour}")
    pdf.parrafo(
        "Retorna el valor de un sensor específico para una estación, fecha y hora."
    )
    pdf.tabla_params([
        ("assignedId", "string", "ID asignado de la estación"),
        ("sensorNumber", "int", "Número de sensor (según orden de sensor/by/station-id)"),
        ("date", "string", "Fecha yyyy-MM-dd"),
        ("hour", "int", "Hora (0-23)"),
    ])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/automatic-station/api/get/sensor-value/by/assigned-id/CFE0000012/sensor-number/6/date/2026-03-24/hour/6"))

    # ==================== LLUVIA ACUMULADA ====================
    pdf.add_page()
    pdf.titulo_seccion("10. Lluvia Acumulada")

    pdf.titulo_endpoint("GET", "/api/get/accumulative-rain/by/id/{stationId}/date/{date}/hour/{hour}")
    pdf.parrafo(
        "Retorna la lluvia acumulada del día hasta la hora indicada para una estación automática."
    )
    pdf.tabla_params([
        ("stationId", "string", "ID asignado de la estación"),
        ("date", "string", "Fecha yyyy-MM-dd"),
        ("hour", "int", "Hora hasta la cual acumular (1-24)"),
    ])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/accumulative-rain/by/id/CFE0000012/date/2026-03-24/hour/6"))
    pdf.label("Respuesta:")
    pdf.code_block(json.dumps({"dateTime":"2026-03-24T06:00:00","rain":0}, indent=2))

    pdf.check_space(60)
    pdf.titulo_endpoint("GET", "/api/get/accumulative-rain/by/assignedId/{assignedId}/vendorId/{vendorId}/date/{date}/hour/{hour}")
    pdf.parrafo(
        "Lluvia acumulada usando tanto el ID asignado como el ID satelital (GOES DCP ID)."
    )
    pdf.tabla_params([
        ("assignedId", "string", "ID asignado"),
        ("vendorId", "string", "ID satelital GOES (DCP ID)"),
        ("date", "string", "Fecha yyyy-MM-dd"),
        ("hour", "int", "Hora (1-24)"),
    ])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/accumulative-rain/by/assignedId/CFE0000012/vendorId/E8908E24/date/2026-03-24/hour/6"))
    pdf.label("Respuesta:")
    pdf.code_block(json.dumps({"dateTime":"2026-03-24T06:00:00","rain":0}, indent=2))

    # ==================== PRONÓSTICO DE LLUVIA ====================
    pdf.add_page()
    pdf.titulo_seccion("11. Pronóstico de Lluvia")

    pdf.titulo_endpoint("GET", "/v1/forecast/last")
    pdf.parrafo("Retorna el último pronóstico de lluvia disponible.")
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/v1/forecast/last"))
    pdf.label("Respuesta:")
    pdf.code_block(json.dumps([
        {"id":"4ad95ace-ab60-48b0-bb8f-ddad7fbf04af","date":"2026-03-31",
         "timestamp":"2026-03-31T22:31:16.0000000Z",
         "lastUpdate":"2026-03-31T22:31:16.0000000Z"}
    ], indent=2, ensure_ascii=False))

    pdf.check_space(60)
    pdf.titulo_endpoint("GET", "/v1/forecast/date/{date}")
    pdf.parrafo("Retorna el pronóstico de lluvia para una fecha específica.")
    pdf.tabla_params([("date", "string", "Fecha yyyy-MM-dd")])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/v1/forecast/date/2026-03-31"))
    pdf.label("Respuesta:")
    pdf.code_block(json.dumps(
        {"id":"4ad95ace-ab60-48b0-bb8f-ddad7fbf04af","date":"2026-03-31",
         "timestamp":"2026-03-31T22:31:16.0000000Z",
         "lastUpdate":"2026-03-31T22:31:16.0000000Z"},
        indent=2, ensure_ascii=False
    ))

    pdf.check_space(80)
    pdf.titulo_endpoint("GET", "/v1/record/forecast-date/{date}/sub-basin-id/{subBasinId}/dates/{start}/{end}")
    pdf.parrafo(
        "Registros de lluvia pronosticada por subcuenca en un rango de fechas ISO 8601."
    )
    pdf.tabla_params([
        ("date", "string", "Fecha del pronóstico yyyy-MM-dd"),
        ("subBasinId", "int", "ID de subcuenca (1-5)"),
        ("start", "string", "Fecha/hora inicio ISO 8601 (ej: 2026-03-31T00:00:00Z)"),
        ("end", "string", "Fecha/hora fin ISO 8601"),
    ])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/v1/record/forecast-date/2026-03-31/sub-basin-id/1/dates/2026-03-31T00:00:00Z/2026-04-01T00:00:00Z"))
    pdf.label("Respuesta (extracto — miles de registros):")
    pdf.code_block(json.dumps([
        {"id":"e4802f78-...","subBasinId":1,"forecastId":"a23ab41a-...",
         "dateTime":"2026-03-31T01:00:00.0000000Z",
         "latitude":16.33,"longitude":-92.46,"rain":0},
        {"note":"... miles de registros de puntos de malla"}
    ], indent=2, ensure_ascii=False))

    # ==================== MODELO HIDROLÓGICO ====================
    pdf.add_page()
    pdf.titulo_seccion("12. Modelo Hidrológico Concentrado")

    pdf.titulo_endpoint("GET", "/api/get/request-input/{date}")
    pdf.parrafo(
        "Genera la estructura de inputs de usuario necesaria para ejecutar el modelo hidrológico. "
        "Retorna un arreglo de objetos con dateTime, date, hour, extraction y extractionPreviousDam "
        "para los siguientes 14 días desde la fecha indicada."
    )
    pdf.tabla_params([("date", "string", "Fecha inicial yyyy-MM-dd")])
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("GET", "/api/get/request-input/2026-03-24"))
    pdf.label("Respuesta (extracto):")
    pdf.code_block(json.dumps({
        "userInput": [
            {"dateTime":"2026-03-24T00:00:00","date":"2026-03-24","hour":0,"extraction":None,"extractionPreviousDam":None},
            {"dateTime":"2026-03-24T01:00:00","date":"2026-03-24","hour":1,"extraction":None,"extractionPreviousDam":None},
            {"note":"... 336 horas (14 días × 24 horas)"}
        ]
    }, indent=2, ensure_ascii=False))

    pdf.check_space(80)
    pdf.titulo_endpoint("POST", "/api/post/concentrated/")
    pdf.parrafo(
        "Ejecuta el modelo hidrológico concentrado (diario u horario según la configuración "
        "de la presa). Recibe el ID de presa, fecha inicial, y opcionalmente los inputs de "
        "usuario (extracciones pronosticadas)."
    )
    pdf.label("Campos del body JSON:")
    pdf.tabla_params([
        ("damId", "int", "ID de la presa (1-5)"),
        ("initialDate", "string", "Fecha inicial yyyy-MM-dd"),
        ("userInput", "array", "Opcional: arreglo de inputs por hora"),
        ("drainBase", "decimal", "Opcional: gasto base para modelo horario"),
        ("drainNumber", "decimal", "Opcional: número de curva (default 80)"),
    ])
    body_example = {
        "damId": 1,
        "initialDate": "2026-03-24",
        "userInput": [
            {"date":"2026-03-24","hour":0,"extraction":300,"extractionPreviousDam":0}
        ],
        "drainBase": 0,
        "drainNumber": 80
    }
    pdf.label("Ejemplo:")
    pdf.code_block(build_curl("POST", "/api/post/concentrated/", body_example))
    pdf.label("Respuesta (extracto):")
    pdf.code_block(json.dumps({
        "subBasinId": 1,
        "modelType": "daily",
        "date": "2026-03-24",
        "dam": {"id":1,"centralId":1,"code":"ANG","description":"Angostura","...":"..."},
        "records": [
            {"date":"2026-03-22","hour":0,"rain":0,"elevation":525.25,"extraction":287.62,
             "basinInput":100,"totalCapacity":15430.19,"isForecast":False},
            {"date":"2026-03-24","hour":0,"rain":0,"elevation":None,"extraction":300,
             "basinInput":None,"totalCapacity":None,"isForecast":True},
            {"note":"... registros diarios con pronóstico"}
        ]
    }, indent=2, ensure_ascii=False))

    # ==================== TABLA RESUMEN ====================
    pdf.add_page()
    pdf.titulo_seccion("13. Tabla Resumen de Endpoints")

    endpoints = [
        ("GET", "/api/get/station/all", "Todas las estaciones"),
        ("GET", "/api/get/station/automatic/all", "Estaciones automáticas"),
        ("GET", "/api/get/station/conventional/all", "Estaciones convencionales"),
        ("GET", "/api/get/station/by/id/{id}", "Estación por ID"),
        ("GET", "/api/get/station/by/central-id/{id}/class/{c}/type/{t}", "Estación por central/clase/tipo"),
        ("GET", "/api/get/station/hydro-model/by/sub-basin/{id}", "Estaciones del modelo hidrológico"),
        ("GET", "/api/get/central/by/id/{id}", "Central hidroeléctrica por ID"),
        ("GET", "/api/get/dam/all", "Todas las presas"),
        ("GET", "/api/get/dam/by/id/{id}", "Presa por ID"),
        ("GET", "/api/get/dam/by/central/{id}", "Presa por central"),
        ("GET", "/api/get/sub-basin/by/id/{id}", "Subcuenca por ID"),
        ("GET", "/api/get/elevation-capacity/.../elevation/{e}", "Capacidad por elevación"),
        ("GET", "/api/get/elevation-capacity/.../capacity/{c}", "Elevación por capacidad"),
        ("GET", "/api/get/dam-behavior/central-id/{id}/date/{d}", "Comportamiento de presa"),
        ("GET", "/api/get/dam-behavior/date/{d}/central-id/{id}", "Comportamiento (ruta alt.)"),
        ("GET", "/api/get/dam-behavior/primary-flow-spending/...", "Gasto de extracción"),
        ("GET", "/api/get/station-report/.../station-id/{id}/date/{d}", "Reporte completo de estación"),
        ("GET", "/api/get/station-report/.../station/{id}/date/{d}/hour/{h}", "Reporte por hora"),
        ("GET", "/automatic-station/.../sensor/by/station-id/{id}", "Sensores de estación"),
        ("GET", "/automatic-station/.../sensor-value/...", "Valor de sensor"),
        ("GET", "/api/get/accumulative-rain/by/id/{id}/date/{d}/hour/{h}", "Lluvia acumulada por ID"),
        ("GET", "/api/get/accumulative-rain/by/assignedId/.../vendorId/...", "Lluvia acumulada por IDs"),
        ("GET", "/v1/forecast/last", "Último pronóstico"),
        ("GET", "/v1/forecast/date/{d}", "Pronóstico por fecha"),
        ("GET", "/v1/record/forecast-date/{d}/sub-basin-id/{id}/dates/...", "Registros de pronóstico"),
        ("GET", "/api/get/request-input/{d}", "Inputs del modelo hidrológico"),
        ("POST", "/api/post/concentrated/", "Ejecutar modelo hidrológico"),
    ]

    pdf.set_font("Segoe", "B", 8)
    pdf.set_fill_color(0, 51, 102)
    pdf.set_text_color(255, 255, 255)
    pdf.cell(15, 6, "Método", border=1, fill=True)
    pdf.cell(110, 6, "Ruta", border=1, fill=True)
    pdf.cell(0, 6, "Descripción", border=1, fill=True, new_x="LMARGIN", new_y="NEXT")

    pdf.set_font("Segoe", "", 7)
    pdf.set_text_color(0, 0, 0)
    fill = False
    for metodo, ruta, desc in endpoints:
        if fill:
            pdf.set_fill_color(245, 245, 245)
        else:
            pdf.set_fill_color(255, 255, 255)
        pdf.cell(15, 4.5, metodo, border=1, fill=True)
        pdf.cell(110, 4.5, ruta, border=1, fill=True)
        pdf.cell(0, 4.5, desc, border=1, fill=True, new_x="LMARGIN", new_y="NEXT")
        fill = not fill

    # ==================== NOTAS FINALES ====================
    pdf.ln(10)
    pdf.titulo_seccion("14. Notas")
    pdf.parrafo(
        "• Los IDs de centrales y presas van del 1 al 5, correspondientes al orden de cascada: "
        "1=Angostura, 2=Chicoasén, 3=Malpaso, 4=Juan Grijalva, 5=Peñitas.\n\n"
        "• Las fechas se manejan en formato yyyy-MM-dd. Las fechas ISO 8601 incluyen la zona horaria.\n\n"
        "• Los datos de funvasos (comportamiento de presa) se actualizan conforme se carga nueva información.\n\n"
        "• El modelo hidrológico concentrado usa el tipo de modelo configurado por presa: "
        "'daily' para Angostura, Malpaso, JGrijalva y Peñitas; 'hourly' para Chicoasén.\n\n"
        "• Las variables de sensor se identifican por nombre en español (precipitación, temperatura, etc.).\n\n"
        "• Los valores de volumen están en Mm³ (millones de metros cúbicos). "
        "Los gastos en m³/s. Las elevaciones en msnm."
    )

    # Save
    out = r"c:\Users\G522X\source\repos\pakoValente77-mx\CloudStationPro\CloudStationWeb\Manual_API_CENACE.pdf"
    pdf.output(out)
    print(f"PDF generado: {out}")


if __name__ == "__main__":
    main()
