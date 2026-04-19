# -*- coding: utf-8 -*-
"""
Genera el manual PDF de automatización de reportes Centinela.
Usa fpdf2 con fuentes Windows (Segoe UI / Consolas).
"""

from fpdf import FPDF
import textwrap

BASE = "http://atlas16.ddns.net:5215"
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
        if self.page_no() == 1:
            return
        self.set_font("Segoe", "B", 9)
        self.set_text_color(100, 100, 100)
        self.cell(0, 6, "PIH — Manual de Automatización de Reportes Centinela",
                  align="C", new_x="LMARGIN", new_y="NEXT")
        self.set_draw_color(180, 180, 180)
        self.line(10, self.get_y(), 200, self.get_y())
        self.ln(3)

    def footer(self):
        self.set_y(-15)
        self.set_font("Segoe", "I", 8)
        self.set_text_color(128, 128, 128)
        self.cell(0, 10, f"Página {self.page_no()}/{{nb}}", align="C")

    def titulo_seccion(self, num, texto):
        self.set_font("Segoe", "B", 15)
        self.set_text_color(0, 51, 102)
        self.ln(4)
        self.cell(0, 10, f"{num}. {texto}", new_x="LMARGIN", new_y="NEXT")
        self.set_draw_color(0, 51, 102)
        self.line(10, self.get_y(), 200, self.get_y())
        self.ln(3)

    def subtitulo(self, texto):
        self.set_font("Segoe", "B", 12)
        self.set_text_color(0, 80, 140)
        self.ln(2)
        self.cell(0, 8, texto, new_x="LMARGIN", new_y="NEXT")
        self.ln(1)

    def parrafo(self, txt):
        self.set_font("Segoe", "", 10)
        self.set_text_color(0, 0, 0)
        self.multi_cell(0, 5.5, txt)
        self.ln(1)

    def bullet(self, txt):
        self.set_font("Segoe", "", 10)
        self.set_text_color(0, 0, 0)
        x = self.get_x()
        self.cell(6, 5.5, "•", new_x="RIGHT", new_y="TOP")
        self.multi_cell(0, 5.5, txt)
        self.set_x(x)

    def label(self, txt):
        self.set_font("Segoe", "B", 10)
        self.set_text_color(60, 60, 60)
        self.cell(0, 7, txt, new_x="LMARGIN", new_y="NEXT")

    def code_block(self, txt):
        self.set_font("Consolas", "", 8)
        self.set_fill_color(240, 240, 240)
        self.set_text_color(30, 30, 30)
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

    def tabla(self, headers, rows, col_widths=None):
        if col_widths is None:
            w = 190 / len(headers)
            col_widths = [w] * len(headers)
        # Header
        self.set_font("Segoe", "B", 9)
        self.set_fill_color(0, 51, 102)
        self.set_text_color(255, 255, 255)
        for i, h in enumerate(headers):
            last = i == len(headers) - 1
            self.cell(col_widths[i], 6, h, border=1, fill=True,
                      new_x="LMARGIN" if last else "RIGHT",
                      new_y="NEXT" if last else "TOP")
        # Rows
        self.set_font("Segoe", "", 9)
        self.set_text_color(0, 0, 0)
        fill = False
        for row in rows:
            if fill:
                self.set_fill_color(245, 245, 245)
            else:
                self.set_fill_color(255, 255, 255)
            for i, val in enumerate(row):
                last = i == len(row) - 1
                self.cell(col_widths[i], 5.5, str(val), border=1, fill=True,
                          new_x="LMARGIN" if last else "RIGHT",
                          new_y="NEXT" if last else "TOP")
            fill = not fill
        self.ln(3)

    def check_space(self, h=60):
        if self.get_y() > 297 - h:
            self.add_page()

    def diagrama_ascii(self, txt):
        self.set_font("Consolas", "", 7.5)
        self.set_text_color(0, 51, 102)
        for line in txt.split("\n"):
            self.cell(0, 3.8, line, new_x="LMARGIN", new_y="NEXT")
        self.set_text_color(0, 0, 0)
        self.ln(3)


def main():
    pdf = ManualPDF()
    pdf.alias_nb_pages()
    pdf.set_auto_page_break(auto=True, margin=20)
    pdf.add_page()

    # ==================== PORTADA ====================
    pdf.ln(35)
    pdf.set_font("Segoe", "B", 32)
    pdf.set_text_color(0, 51, 102)
    pdf.cell(0, 16, "PIH", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.set_font("Segoe", "B", 18)
    pdf.cell(0, 12, "Plataforma Integral Hidrometeorológica", align="C",
             new_x="LMARGIN", new_y="NEXT")
    pdf.ln(6)
    pdf.set_font("Segoe", "", 16)
    pdf.set_text_color(80, 80, 80)
    pdf.cell(0, 10, "Manual de Automatización de Reportes",
             align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.cell(0, 10, "Bot Centinela", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.ln(8)
    pdf.set_font("Segoe", "", 12)
    pdf.set_text_color(100, 100, 100)
    pdf.cell(0, 8, "Catálogo Dinámico + Auto-Upload de Reportes /1 al /7",
             align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.cell(0, 8, "Cuenca del Río Grijalva — CFE SPH",
             align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.ln(16)
    pdf.set_font("Segoe", "I", 11)
    pdf.set_text_color(120, 120, 120)
    pdf.cell(0, 8, f"Servidor: {BASE}", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.cell(0, 8, "Abril 2026", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.ln(20)
    pdf.set_font("Segoe", "", 10)
    pdf.set_text_color(0, 0, 0)
    pdf.multi_cell(0, 6, (
        "Este documento describe cómo desplegar, configurar y automatizar "
        "la actualización de los reportes que el bot Centinela sirve en la plataforma PIH.\n\n"
        "Incluye: migración de base de datos, API CRUD del catálogo, "
        "instalación del cliente auto-upload (ReportUploader), "
        "y ejemplos completos con curl."
    ), align="C")

    # ==================== ÍNDICE ====================
    pdf.add_page()
    pdf.titulo_seccion("", "Índice")
    items = [
        "1. Arquitectura del Sistema",
        "2. Requisitos",
        "3. Migración de Base de Datos",
        "4. API del Catálogo de Reportes",
        "5. Instalar ReportUploader",
        "6. Configurar Directorio de Reportes",
        "7. Agregar Nuevos Reportes",
        "8. Administración del Catálogo (Ejemplos CRUD)",
        "9. Monitoreo y Troubleshooting",
        "10. Referencia de Archivos",
    ]
    for item in items:
        pdf.bullet(item)
    pdf.ln(5)

    # ==================== 1. ARQUITECTURA ====================
    pdf.add_page()
    pdf.titulo_seccion("1", "Arquitectura del Sistema")
    pdf.parrafo(
        "El sistema de reportes funciona con tres componentes principales:"
    )
    pdf.bullet("Fuente de reportes: scripts o capturas que generan imágenes PNG cada ~1 minuto")
    pdf.bullet("ReportUploader: cliente que monitorea un directorio y sube archivos al servidor cada 60s")
    pdf.bullet("CloudStationWeb: servidor que almacena imágenes y el catálogo en SQL Server")
    pdf.ln(4)

    pdf.label("Diagrama de flujo:")
    pdf.diagrama_ascii("""\
  ┌───────────────────┐     escribe PNG      ┌───────────────────┐
  │  Script / Captura  │ ──────────────────▶ │  C:\\PIH\\reportes\\  │
  │  (cada ~1 minuto)  │                     │  (directorio watch)│
  └───────────────────┘                      └────────┬──────────┘
                                                      │ detecta cambios
                                                      ▼
                                             ┌───────────────────┐
                                             │  ReportUploader    │  GET /api/reports
                                             │  (cada 60s)        │◀─ (catálogo dinámico)
                                             └────────┬──────────┘
                                                      │ POST /api/images/unidades?name=X
                                                      ▼
                                             ┌───────────────────┐
                                             │  CloudStationWeb   │
                                             │  ├─ ImageStore/    │ ← archivos físicos
                                             │  └─ SQL Server     │ ← catálogo BD
                                             └────────┬──────────┘
                                                      │
                                  ┌───────────────────┼───────────────────┐
                                  ▼                   ▼                   ▼
                             ┌─────────┐         ┌────────┐         ┌─────────┐
                             │Centinela│         │Web/API │         │iOS/Andr.│
                             │ /1../7  │         │clientes│         │Desktop  │
                             └─────────┘         └────────┘         └─────────┘""")

    # ==================== 2. REQUISITOS ====================
    pdf.titulo_seccion("2", "Requisitos")
    pdf.tabla(
        ["Componente", "Versión", "Ubicación"],
        [
            [".NET Runtime", "8.0+", "Servidor producción"],
            ["SQL Server", "2016+", "atlas16.ddns.net / IGSCLOUD"],
            ["CloudStationWeb", "Última publicación", "IIS en producción"],
            ["sqlcmd", "Cualquiera", "Servidor producción"],
            ["ReportUploader", ".NET 8 console app", "C:\\PIH\\ReportUploader\\"],
        ],
        [50, 40, 100]
    )

    # ==================== 3. MIGRACIÓN ====================
    pdf.add_page()
    pdf.titulo_seccion("3", "Migración de Base de Datos")
    pdf.parrafo(
        "La tabla ReportDefinitions almacena el catálogo de reportes. "
        "Se proveen tres opciones de migración:"
    )

    pdf.subtitulo("Opción A: SQL directo")
    pdf.code_block(
        'sqlcmd -S atlas16.ddns.net -d IGSCLOUD -U sa -P "***REDACTED-SQL-PASSWORD***" \\\n'
        '  -i deploy_report_catalog.sql'
    )

    pdf.subtitulo("Opción B: Script automatizado (SQL + publish + IIS)")
    pdf.code_block(
        'cd CloudStationWeb\n'
        'actualizar_catalogo_reportes.bat'
    )
    pdf.parrafo("Este script ejecuta la migración SQL, publica Release y reinicia el sitio IIS.")

    pdf.subtitulo("Opción C: EF Core (desde desarrollo)")
    pdf.code_block(
        'cd CloudStationWeb\n'
        'dotnet ef database update'
    )

    pdf.subtitulo("Estructura de la tabla ReportDefinitions")
    pdf.tabla(
        ["Columna", "Tipo", "Descripción"],
        [
            ["Id", "INT (PK)", "Identificador auto-incremental"],
            ["Command", "NVARCHAR(20)", "Comando del bot: /1, /2, ... /7"],
            ["ContentType", "NVARCHAR(50)", "Tipo: image, chart, text, document"],
            ["Title", "NVARCHAR(200)", "Título del reporte"],
            ["Description", "NVARCHAR(500)", "Descripción opcional"],
            ["Category", "NVARCHAR(50)", "Categoría ImageStore: unidades, charts, lluvia, general"],
            ["BlobName", "NVARCHAR(500)", "Nombre fijo del archivo (ej: GUID.png)"],
            ["LatestPrefix", "NVARCHAR(200)", "Prefijo para búsqueda dinámica (reportes lluvia)"],
            ["Caption", "NVARCHAR(500)", "Texto/emoji mostrado al usuario en el chat"],
            ["IsActive", "BIT", "Activo en el catálogo (default: 1)"],
            ["SortOrder", "INT", "Orden de presentación"],
            ["CreatedAt", "DATETIME2", "Fecha de creación"],
            ["UpdatedAt", "DATETIME2", "Última modificación"],
        ],
        [35, 40, 115]
    )

    pdf.check_space(50)
    pdf.subtitulo("Datos seed (7 reportes iniciales)")
    pdf.tabla(
        ["Cmd", "Título", "BlobName / Prefijo"],
        [
            ["/1", "Reporte de Unidades", "9c8a7f42-...-6f7d.png"],
            ["/2", "Power Monitoring", "6f3b2c91-...-e24a.png"],
            ["/3", "Gráfica de Potencia", "b7e1f9c3-...-2c01.png"],
            ["/4", "Condición de Embalses", "e1a5f734-...-9b8f.png"],
            ["/5", "Aportaciones Cuenca", "d42f3e19-...-2c01.png"],
            ["/6", "Lluvias 24h", "prefix: reporte_lluvia_1_1_"],
            ["/7", "Lluvias Parcial", "prefix: reporte_lluvia_1_2_"],
        ],
        [15, 55, 120]
    )

    # ==================== 4. API DEL CATÁLOGO ====================
    pdf.add_page()
    pdf.titulo_seccion("4", "API del Catálogo de Reportes")
    pdf.parrafo(
        "La API permite consultar y administrar el catálogo de reportes. "
        "Los endpoints de lectura son públicos; los de escritura requieren X-Api-Key."
    )

    # GET /api/reports
    pdf.subtitulo("GET /api/reports — Listar reportes activos")
    pdf.parrafo("Retorna todos los reportes con IsActive=true, ordenados por SortOrder. No requiere autenticación.")
    pdf.label("Ejemplo curl:")
    pdf.code_block(f'curl "{BASE}/api/reports"')
    pdf.label("Respuesta:")
    pdf.code_block("""\
[
  {
    "id": 1,
    "command": "/1",
    "contentType": "image",
    "title": "Reporte de Unidades",
    "category": "unidades",
    "blobName": "9c8a7f42-3d91-4e01-a3fa-0d2e5b1c6f7d.png",
    "latestPrefix": null,
    "caption": "📊 Reporte de Unidades actualizado.",
    "isActive": true,
    "sortOrder": 1,
    "createdAt": "2025-01-01T00:00:00Z",
    "updatedAt": "2025-01-01T00:00:00Z"
  },
  ...
]""")

    # GET /api/reports/all
    pdf.check_space(50)
    pdf.subtitulo("GET /api/reports/all — Todos (incluye inactivos)")
    pdf.parrafo("Requiere header X-Api-Key.")
    pdf.label("Ejemplo:")
    pdf.code_block(f'curl -H "X-Api-Key: {API_KEY}" "{BASE}/api/reports/all"')

    # GET /api/reports/{id}
    pdf.subtitulo("GET /api/reports/{{id}} — Detalle por Id")
    pdf.code_block(f'curl "{BASE}/api/reports/1"')

    # POST /api/reports
    pdf.check_space(80)
    pdf.subtitulo("POST /api/reports — Crear reporte")
    pdf.parrafo("Requiere X-Api-Key. Body JSON con campos del reporte.")
    pdf.label("Ejemplo: Agregar comando /9")
    pdf.code_block(f"""\
curl -X POST -H "X-Api-Key: {API_KEY}" \\
  -H "Content-Type: application/json" \\
  -d '{{
    "command": "/9",
    "contentType": "image",
    "title": "Reporte CENACE",
    "category": "unidades",
    "blobName": "reporte_cenace.png",
    "caption": "📊 Reporte CENACE actualizado.",
    "sortOrder": 9
  }}' \\
  "{BASE}/api/reports" """)

    pdf.label("Respuesta (201 Created):")
    pdf.code_block("""\
{
  "id": 8,
  "command": "/9",
  "contentType": "image",
  "title": "Reporte CENACE",
  "isActive": true,
  ...
}""")

    # PUT /api/reports/{id}
    pdf.add_page()
    pdf.subtitulo("PUT /api/reports/{{id}} — Actualizar reporte")
    pdf.parrafo("Solo enviar los campos a modificar. Requiere X-Api-Key.")
    pdf.label("Ejemplo: Cambiar caption del reporte /1")
    pdf.code_block(f"""\
curl -X PUT -H "X-Api-Key: {API_KEY}" \\
  -H "Content-Type: application/json" \\
  -d '{{ "caption": "📊 Nuevo caption para el reporte." }}' \\
  "{BASE}/api/reports/1" """)

    pdf.label("Ejemplo: Desactivar un reporte (sin eliminar)")
    pdf.code_block(f"""\
curl -X PUT -H "X-Api-Key: {API_KEY}" \\
  -H "Content-Type: application/json" \\
  -d '{{ "isActive": false }}' \\
  "{BASE}/api/reports/3" """)

    # DELETE /api/reports/{id}
    pdf.subtitulo("DELETE /api/reports/{{id}} — Eliminar reporte")
    pdf.code_block(f"""\
curl -X DELETE -H "X-Api-Key: {API_KEY}" \\
  "{BASE}/api/reports/3" """)
    pdf.label("Respuesta:")
    pdf.code_block('{ "message": "Reporte \'/3\' eliminado" }')

    # ==================== 5. INSTALAR REPORTUPLOADER ====================
    pdf.add_page()
    pdf.titulo_seccion("5", "Instalar ReportUploader")
    pdf.parrafo(
        "ReportUploader es una aplicación de consola .NET 8 que monitorea un directorio "
        "y sube automáticamente los archivos modificados al servidor PIH cada 60 segundos."
    )

    pdf.subtitulo("Opción A: Instalador automático (recomendado)")
    pdf.parrafo("Ejecutar como Administrador:")
    pdf.code_block("instalar_reportuploader_tarea.bat")
    pdf.parrafo("Este script:")
    pdf.bullet("Compila y publica ReportUploader en C:\\PIH\\ReportUploader\\")
    pdf.bullet("Crea el directorio C:\\PIH\\reportes\\ para los archivos fuente")
    pdf.bullet("Registra tarea programada PIH_ReportUploader (inicio con Windows)")
    pdf.bullet("Inicia el servicio inmediatamente")

    pdf.subtitulo("Opción B: Ejecución manual")
    pdf.code_block("""\
REM Compilar
dotnet publish ReportUploader\\ReportUploader.csproj -c Release -o C:\\PIH\\ReportUploader

REM Ejecutar
C:\\PIH\\ReportUploader\\ReportUploader.exe ^
    --server http://atlas16.ddns.net:5215 ^
    --key ***REDACTED-API-KEY*** ^
    --watch C:\\PIH\\reportes ^
    --interval 60""")

    pdf.subtitulo("Parámetros")
    pdf.tabla(
        ["Parámetro", "Default", "Descripción"],
        [
            ["--server", "http://localhost:5215", "URL del servidor PIH"],
            ["--key", "***REDACTED-API-KEY***", "API key de ImageStore"],
            ["--watch", "./reportes", "Directorio a monitorear"],
            ["--interval", "60", "Segundos entre ciclos de upload"],
        ],
        [35, 55, 100]
    )

    pdf.subtitulo("Salida esperada")
    pdf.code_block("""\
╔══════════════════════════════════════════════════╗
║     ReportUploader — PIH Auto-Upload Service    ║
╠══════════════════════════════════════════════════╣
║  Servidor : http://atlas16.ddns.net:5215        ║
║  Watch    : C:\\PIH\\reportes                     ║
║  Intervalo:  60s                                ║
╚══════════════════════════════════════════════════╝

[14:30:00] Ciclo: 3 subidos, 4 sin cambios, 7 reportes en catálogo
  ✓ /1 (Reporte de Unidades) → 9c8a7f42-...-6f7d.png
  ✓ /6 (Reporte de Lluvias 24h) → reporte_lluvia_1_1_638848218556433423.png
  ✓ /7 (Reporte Parcial) → reporte_lluvia_1_2_638848218556433423.png""")

    # ==================== 6. DIRECTORIO DE REPORTES ====================
    pdf.add_page()
    pdf.titulo_seccion("6", "Configurar Directorio de Reportes")
    pdf.parrafo(
        "El directorio C:\\PIH\\reportes\\ debe contener los archivos con esta nomenclatura. "
        "ReportUploader solo sube archivos que hayan cambiado desde la última subida "
        "(compara LastWriteTime del archivo)."
    )

    pdf.subtitulo("Estructura esperada")
    pdf.code_block("""\
C:\\PIH\\reportes\\
├── 1.png                              → Comando /1 (Reporte de Unidades)
├── 2.png                              → Comando /2 (Power Monitoring)
├── 3.png                              → Comando /3 (Gráfica de Potencia)
├── 4.png                              → Comando /4 (Condición de Embalses)
├── 5.png                              → Comando /5 (Aportaciones Cuenca)
├── reporte_lluvia_1_1_TIMESTAMP.png   → Comando /6 (Lluvias 24h)
└── reporte_lluvia_1_2_TIMESTAMP.png   → Comando /7 (Lluvias parcial)""")

    pdf.subtitulo("Reglas de matching (prioridad)")
    pdf.tabla(
        ["Prioridad", "Regla", "Ejemplo"],
        [
            ["1", "Por número de comando", "1.png → /1, 2.png → /2"],
            ["2", "Por prefijo (LatestPrefix)", "reporte_lluvia_1_1_*.png → /6"],
            ["3", "Por nombre exacto (BlobName)", "9c8a7f42-...-6f7d.png → /1"],
        ],
        [25, 55, 110]
    )

    pdf.subtitulo("Ejemplo: Script que genera reportes cada minuto")
    pdf.label("PowerShell:")
    pdf.code_block("""\
# Copiar capturas al directorio watch cada 60 segundos
while ($true) {
    Copy-Item "C:\\capturas\\reporte_unidades.png"  "C:\\PIH\\reportes\\1.png" -Force
    Copy-Item "C:\\capturas\\power_monitoring.png"   "C:\\PIH\\reportes\\2.png" -Force
    Copy-Item "C:\\capturas\\grafica_potencia.png"   "C:\\PIH\\reportes\\3.png" -Force
    Copy-Item "C:\\capturas\\condicion_embalses.png" "C:\\PIH\\reportes\\4.png" -Force
    Copy-Item "C:\\capturas\\aportaciones.png"       "C:\\PIH\\reportes\\5.png" -Force
    # Para lluvia: copiar con prefijo + timestamp
    $ts = [DateTime]::UtcNow.Ticks
    Copy-Item "C:\\capturas\\lluvia_24h.png" \\
        "C:\\PIH\\reportes\\reporte_lluvia_1_1_$ts.png" -Force
    Copy-Item "C:\\capturas\\lluvia_parcial.png" \\
        "C:\\PIH\\reportes\\reporte_lluvia_1_2_$ts.png" -Force
    Start-Sleep -Seconds 60
}""")

    pdf.label("Python:")
    pdf.code_block("""\
import shutil, time, datetime
while True:
    shutil.copy2("capturas/reporte_unidades.png", "C:/PIH/reportes/1.png")
    shutil.copy2("capturas/power_monitoring.png",  "C:/PIH/reportes/2.png")
    # ... etc.
    ts = datetime.datetime.utcnow().strftime("%Y%m%d%H%M%S")
    shutil.copy2("capturas/lluvia_24h.png",
                 f"C:/PIH/reportes/reporte_lluvia_1_1_{ts}.png")
    time.sleep(60)""")

    # ==================== 7. AGREGAR NUEVOS REPORTES ====================
    pdf.add_page()
    pdf.titulo_seccion("7", "Agregar Nuevos Reportes")
    pdf.parrafo(
        "Para agregar un reporte nuevo (ej. /9) que se actualice automáticamente, "
        "se necesitan solo 2 pasos. No se requiere reiniciar el servidor."
    )

    pdf.subtitulo("Paso 1: Registrar en el catálogo via API")
    pdf.code_block(f"""\
curl -X POST -H "X-Api-Key: {API_KEY}" \\
  -H "Content-Type: application/json" \\
  -d '{{
    "command": "/9",
    "contentType": "image",
    "title": "Mi Nuevo Reporte",
    "category": "unidades",
    "blobName": "mi_reporte.png",
    "caption": "📊 Mi nuevo reporte actualizado.",
    "sortOrder": 9
  }}' \\
  "{BASE}/api/reports" """)

    pdf.subtitulo("Paso 2: Colocar archivo en directorio watch")
    pdf.code_block("copy mi_reporte.png C:\\PIH\\reportes\\9.png")

    pdf.subtitulo("Resultado")
    pdf.bullet("ReportUploader lo sube en el siguiente ciclo (máximo 60 segundos)")
    pdf.bullet("Centinela recarga el catálogo de BD cada 5 minutos automáticamente")
    pdf.bullet("El bot responde a /9 con la imagen nueva")
    pdf.bullet("La API sirve la imagen en GET /api/images/unidades/mi_reporte.png")

    pdf.ln(5)
    pdf.label("Ejemplo con prefijo (reporte que cambia nombre cada vez):")
    pdf.code_block(f"""\
curl -X POST -H "X-Api-Key: {API_KEY}" \\
  -H "Content-Type: application/json" \\
  -d '{{
    "command": "/10",
    "contentType": "image",
    "title": "Pronóstico Cuenca Grijalva",
    "category": "unidades",
    "latestPrefix": "pronostico_grijalva_",
    "caption": "🌧️ Pronóstico actualizado de cuenca Grijalva.",
    "sortOrder": 10
  }}' \\
  "{BASE}/api/reports" """)
    pdf.parrafo(
        "Luego colocar archivos como pronostico_grijalva_20260417.png en el directorio watch. "
        "ReportUploader buscará por prefijo y subirá el más reciente."
    )

    # ==================== 8. ADMINISTRACIÓN CRUD ====================
    pdf.add_page()
    pdf.titulo_seccion("8", "Administración del Catálogo (Ejemplos CRUD)")

    pdf.subtitulo("Listar catálogo activo")
    pdf.code_block(f'curl "{BASE}/api/reports" | python -m json.tool')

    pdf.subtitulo("Ver detalle de un reporte")
    pdf.code_block(f'curl "{BASE}/api/reports/1" | python -m json.tool')

    pdf.subtitulo("Crear nuevo reporte")
    pdf.code_block(f"""\
curl -X POST "{BASE}/api/reports" \\
  -H "X-Api-Key: {API_KEY}" \\
  -H "Content-Type: application/json" \\
  -d '{{ "command": "/8", "title": "CENACE", "blobName": "cenace.png", \\
        "caption": "📊 Reporte CENACE.", "sortOrder": 8 }}'""")

    pdf.subtitulo("Actualizar caption")
    pdf.code_block(f"""\
curl -X PUT "{BASE}/api/reports/1" \\
  -H "X-Api-Key: {API_KEY}" \\
  -H "Content-Type: application/json" \\
  -d '{{ "caption": "📊 Nuevo texto del Reporte de Unidades." }}'""")

    pdf.subtitulo("Desactivar reporte (sin eliminar)")
    pdf.code_block(f"""\
curl -X PUT "{BASE}/api/reports/3" \\
  -H "X-Api-Key: {API_KEY}" \\
  -H "Content-Type: application/json" \\
  -d '{{ "isActive": false }}'""")

    pdf.subtitulo("Reactivar reporte")
    pdf.code_block(f"""\
curl -X PUT "{BASE}/api/reports/3" \\
  -H "X-Api-Key: {API_KEY}" \\
  -H "Content-Type: application/json" \\
  -d '{{ "isActive": true }}'""")

    pdf.subtitulo("Eliminar reporte permanentemente")
    pdf.code_block(f"""\
curl -X DELETE "{BASE}/api/reports/3" \\
  -H "X-Api-Key: {API_KEY}" """)

    pdf.subtitulo("Subir imagen manualmente (sin ReportUploader)")
    pdf.code_block(f"""\
curl -X POST "{BASE}/api/images/unidades?name=9c8a7f42-3d91-4e01-a3fa-0d2e5b1c6f7d.png" \\
  -H "X-Api-Key: {API_KEY}" \\
  -F "file=@reporte_unidades.png" """)

    # ==================== 9. TROUBLESHOOTING ====================
    pdf.add_page()
    pdf.titulo_seccion("9", "Monitoreo y Troubleshooting")

    pdf.subtitulo("Comandos de la tarea programada")
    pdf.label("Ver estado:")
    pdf.code_block('schtasks /query /tn "PIH_ReportUploader" /v')
    pdf.label("Detener:")
    pdf.code_block('schtasks /end /tn "PIH_ReportUploader"')
    pdf.label("Iniciar:")
    pdf.code_block('schtasks /run /tn "PIH_ReportUploader"')
    pdf.label("Eliminar:")
    pdf.code_block('schtasks /delete /tn "PIH_ReportUploader" /f')

    pdf.subtitulo("Verificar catálogo en SQL Server")
    pdf.code_block("""\
sqlcmd -S atlas16.ddns.net -d IGSCLOUD -U sa \\
  -Q "SELECT Command, Title, BlobName, LatestPrefix, IsActive
      FROM ReportDefinitions ORDER BY SortOrder" """)

    pdf.subtitulo("Problemas comunes")
    pdf.tabla(
        ["Problema", "Causa", "Solución"],
        [
            ["API key inválida", "Header X-Api-Key incorrecto", "Usar: ***REDACTED-API-KEY***"],
            ["Categoría no permitida", "Categoría no existe", "Usar: unidades, charts, lluvia, general"],
            ["Bot no muestra reporte nuevo", "Cache de 5 minutos", "Esperar 5 min o reiniciar web"],
            ["No conecta al servidor", "URL incorrecta o servidor caído", "Verificar URL y estado de IIS"],
            ["Archivo no se sube", "Sin cambio en LastWriteTime", "Reescribir o tocar el archivo"],
            ["Tarea no inicia", "Sin permisos de admin", "Ejecutar instalador como Admin"],
        ],
        [45, 50, 95]
    )

    pdf.subtitulo("Cache de Centinela")
    pdf.parrafo(
        "El bot Centinela cachea el catálogo de reportes en memoria por 5 minutos. "
        "Si se agrega un reporte nuevo via API, Centinela lo reflejará en máximo 5 minutos "
        "sin necesidad de reiniciar."
    )

    # ==================== 10. REFERENCIA ====================
    pdf.add_page()
    pdf.titulo_seccion("10", "Referencia de Archivos")
    pdf.tabla(
        ["Archivo", "Propósito"],
        [
            ["Models/ReportDefinition.cs", "Modelo EF Core del catálogo"],
            ["Controllers/ReportCatalogController.cs", "API CRUD /api/reports"],
            ["Data/ApplicationDbContext.cs", "DbSet + seed de 7 reportes"],
            ["Services/CentinelaBotService.cs", "Lee catálogo de BD (cache 5 min)"],
            ["deploy_report_catalog.sql", "Script SQL de migración manual"],
            ["actualizar_catalogo_reportes.bat", "Script todo-en-uno producción"],
            ["ReportUploader/Program.cs", "Cliente auto-upload (.NET 8)"],
            ["instalar_reportuploader_tarea.bat", "Instalador tarea programada Windows"],
        ],
        [75, 115]
    )

    pdf.subtitulo("Endpoints resumen")
    pdf.tabla(
        ["Método", "Endpoint", "Auth", "Descripción"],
        [
            ["GET", "/api/reports", "Público", "Reportes activos"],
            ["GET", "/api/reports/all", "API Key", "Todos los reportes"],
            ["GET", "/api/reports/{id}", "Público", "Detalle por Id"],
            ["POST", "/api/reports", "API Key", "Crear reporte"],
            ["PUT", "/api/reports/{id}", "API Key", "Actualizar reporte"],
            ["DELETE", "/api/reports/{id}", "API Key", "Eliminar reporte"],
            ["POST", "/api/images/{cat}", "API Key", "Subir imagen"],
            ["GET", "/api/images/{cat}/{file}", "Público", "Descargar imagen"],
        ],
        [20, 52, 22, 96]
    )

    # ==================== GUARDAR ====================
    out = "Manual_Automatizacion_Reportes.pdf"
    pdf.output(out)
    print(f"\n✅ PDF generado: {out}")
    print(f"   Páginas: {pdf.pages_count}")


if __name__ == "__main__":
    main()
