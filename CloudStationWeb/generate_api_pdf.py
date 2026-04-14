#!/usr/bin/env python3
"""
Genera documento PDF profesional de la API de Pronóstico Hidrológico.
Estilo oficial CFE (Comisión Federal de Electricidad).

Uso: python3 generate_api_pdf.py
"""

import os
from datetime import datetime
from reportlab.lib import colors
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch, cm
from reportlab.lib.enums import TA_CENTER, TA_LEFT, TA_JUSTIFY, TA_RIGHT
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle,
    PageBreak, HRFlowable, KeepTogether, ListFlowable, ListItem
)
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont

# ── Colores CFE ──────────────────────────────────────────────
CFE_GREEN = colors.HexColor("#006847")
CFE_DARK = colors.HexColor("#1a1a2e")
CFE_ACCENT = colors.HexColor("#00a86b")
CFE_LIGHT_GREEN = colors.HexColor("#e8f5e9")
CFE_GRAY = colors.HexColor("#f5f5f5")
CFE_TEXT = colors.HexColor("#333333")
CFE_HEADER_BG = colors.HexColor("#006847")
CFE_ROW_ALT = colors.HexColor("#f0faf0")
CODE_BG = colors.HexColor("#1e1e1e")
CODE_TEXT = colors.HexColor("#d4d4d4")
CFE_RED = colors.HexColor("#c62828")
CFE_GOLD = colors.HexColor("#c49000")

OUTPUT_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "API_Pronostico_Hidrologico_CFE.pdf")


def build_styles():
    styles = getSampleStyleSheet()

    styles.add(ParagraphStyle(
        "CoverTitle", parent=styles["Title"],
        fontSize=28, leading=34, textColor=colors.white,
        alignment=TA_CENTER, spaceAfter=6
    ))
    styles.add(ParagraphStyle(
        "CoverSubtitle", parent=styles["Normal"],
        fontSize=14, leading=18, textColor=colors.white,
        alignment=TA_CENTER, spaceAfter=6
    ))
    styles.add(ParagraphStyle(
        "CoverOrg", parent=styles["Normal"],
        fontSize=11, leading=14, textColor=colors.HexColor("#b0e0b0"),
        alignment=TA_CENTER
    ))
    styles.add(ParagraphStyle(
        "SectionTitle", parent=styles["Heading1"],
        fontSize=18, leading=22, textColor=CFE_GREEN,
        spaceBefore=20, spaceAfter=10,
        borderWidth=0, borderPadding=0,
        borderColor=CFE_GREEN
    ))
    styles.add(ParagraphStyle(
        "SubSectionTitle", parent=styles["Heading2"],
        fontSize=14, leading=17, textColor=CFE_DARK,
        spaceBefore=14, spaceAfter=6
    ))
    styles.add(ParagraphStyle(
        "BodyText2", parent=styles["Normal"],
        fontSize=10, leading=14, textColor=CFE_TEXT,
        alignment=TA_JUSTIFY, spaceAfter=6
    ))
    styles.add(ParagraphStyle(
        "CodeBlock", parent=styles["Normal"],
        fontSize=8, leading=11, textColor=CODE_TEXT,
        backColor=CODE_BG, leftIndent=10, rightIndent=10,
        spaceBefore=4, spaceAfter=8,
        fontName="Courier", borderWidth=0.5,
        borderColor=colors.HexColor("#444"), borderPadding=8
    ))
    styles.add(ParagraphStyle(
        "FooterStyle", parent=styles["Normal"],
        fontSize=8, leading=10, textColor=colors.gray, alignment=TA_CENTER
    ))
    styles.add(ParagraphStyle(
        "SmallNote", parent=styles["Normal"],
        fontSize=9, leading=12, textColor=colors.HexColor("#666"),
        spaceAfter=4, fontName="Helvetica-Oblique"
    ))
    styles.add(ParagraphStyle(
        "TableHeader", parent=styles["Normal"],
        fontSize=9, leading=12, textColor=colors.white,
        fontName="Helvetica-Bold", alignment=TA_CENTER
    ))
    styles.add(ParagraphStyle(
        "TableCell", parent=styles["Normal"],
        fontSize=9, leading=12, textColor=CFE_TEXT, alignment=TA_LEFT
    ))
    styles.add(ParagraphStyle(
        "BulletItem", parent=styles["Normal"],
        fontSize=10, leading=14, textColor=CFE_TEXT,
        leftIndent=20, bulletIndent=8, spaceAfter=3
    ))
    return styles


def header_footer(canvas, doc):
    """Dibuja encabezado y pie en cada página (excepto portada)."""
    if doc.page == 1:
        return
    canvas.saveState()
    # Header bar
    canvas.setFillColor(CFE_GREEN)
    canvas.rect(0, letter[1] - 35, letter[0], 35, fill=1, stroke=0)
    canvas.setFillColor(colors.white)
    canvas.setFont("Helvetica-Bold", 9)
    canvas.drawString(inch, letter[1] - 23, "CFE — API de Pronóstico Hidrológico · Sistema Grijalva")
    canvas.drawRightString(letter[0] - inch, letter[1] - 23, "CONFIDENCIAL")

    # Footer
    canvas.setFillColor(CFE_GREEN)
    canvas.rect(0, 0, letter[0], 25, fill=1, stroke=0)
    canvas.setFillColor(colors.white)
    canvas.setFont("Helvetica", 8)
    canvas.drawString(inch, 9.5, f"Generado: {datetime.now().strftime('%Y-%m-%d %H:%M')}")
    canvas.drawRightString(letter[0] - inch, 9.5, f"Página {doc.page}")
    canvas.drawCentredString(letter[0] / 2, 9.5, "Subgerencia Técnica · Hidroeléctrica Grijalva")
    canvas.restoreState()


def make_table(data, col_widths=None, header_row=True):
    """Crea tabla estilizada con colores CFE."""
    style_cmds = [
        ("GRID", (0, 0), (-1, -1), 0.5, colors.HexColor("#ccc")),
        ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
        ("LEFTPADDING", (0, 0), (-1, -1), 6),
        ("RIGHTPADDING", (0, 0), (-1, -1), 6),
        ("TOPPADDING", (0, 0), (-1, -1), 4),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
    ]
    if header_row:
        style_cmds += [
            ("BACKGROUND", (0, 0), (-1, 0), CFE_HEADER_BG),
            ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
            ("FONTNAME", (0, 0), (-1, 0), "Helvetica-Bold"),
            ("FONTSIZE", (0, 0), (-1, 0), 9),
        ]
        for i in range(1, len(data)):
            if i % 2 == 0:
                style_cmds.append(("BACKGROUND", (0, i), (-1, i), CFE_ROW_ALT))

    t = Table(data, colWidths=col_widths, repeatRows=1 if header_row else 0)
    t.setStyle(TableStyle(style_cmds))
    return t


def code_block(text, styles):
    """Bloque de código con fondo oscuro."""
    return Paragraph(text.replace("\n", "<br/>").replace(" ", "&nbsp;"), styles["CodeBlock"])


def green_divider():
    return HRFlowable(width="100%", thickness=2, color=CFE_GREEN, spaceBefore=4, spaceAfter=8)


def build_cover(elements, styles):
    """Portada oficial CFE."""
    elements.append(Spacer(1, 2.5 * inch))

    # Green band
    cover_data = [[""]]
    cover_table = Table(cover_data, colWidths=[6.5 * inch], rowHeights=[3.5 * inch])
    cover_table.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), CFE_GREEN),
        ("ALIGN", (0, 0), (-1, -1), "CENTER"),
        ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
    ]))

    inner_content = []
    inner_content.append(Paragraph("COMISIÓN FEDERAL DE ELECTRICIDAD", styles["CoverOrg"]))
    inner_content.append(Spacer(1, 6))
    inner_content.append(Paragraph("Subgerencia Técnica · Hidroeléctrica Grijalva", styles["CoverOrg"]))
    inner_content.append(Spacer(1, 20))
    inner_content.append(Paragraph("API de Pronóstico<br/>Hidrológico", styles["CoverTitle"]))
    inner_content.append(Spacer(1, 12))
    inner_content.append(Paragraph("Documentación Técnica de Referencia", styles["CoverSubtitle"]))
    inner_content.append(Spacer(1, 8))
    inner_content.append(Paragraph("Sistema de Cascada Grijalva — CloudStation", styles["CoverSubtitle"]))
    inner_content.append(Spacer(1, 20))
    inner_content.append(Paragraph(f"Versión 2.0 — {datetime.now().strftime('%B %Y')}", styles["CoverOrg"]))

    inner_table_data = [[inner_content]]
    inner_table = Table(inner_table_data, colWidths=[6 * inch])
    inner_table.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), CFE_GREEN),
        ("ALIGN", (0, 0), (-1, -1), "CENTER"),
        ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
        ("LEFTPADDING", (0, 0), (-1, -1), 30),
        ("RIGHTPADDING", (0, 0), (-1, -1), 30),
        ("TOPPADDING", (0, 0), (-1, -1), 30),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 30),
    ]))

    elements.append(inner_table)
    elements.append(Spacer(1, 1 * inch))

    # Metadata
    meta_style = ParagraphStyle("meta", fontSize=10, leading=13, textColor=CFE_TEXT, alignment=TA_CENTER)
    elements.append(Paragraph("DOCUMENTO CONFIDENCIAL — USO INTERNO CFE", meta_style))
    elements.append(Spacer(1, 6))
    elements.append(Paragraph(f"Fecha de generación: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}", meta_style))
    elements.append(PageBreak())


def build_toc(elements, styles):
    """Tabla de contenidos."""
    elements.append(Paragraph("Contenido", styles["SectionTitle"]))
    elements.append(green_divider())

    toc_items = [
        ("1.", "Información General", "3"),
        ("2.", "Autenticación y Roles", "3"),
        ("3.", "Rol ApiConsumer", "4"),
        ("4.", "Endpoints", "5"),
        ("4.1", "  Catálogo de Presas (GET /api/hydro/dams)", "5"),
        ("4.2", "  Datos de Entrada (GET /api/hydro/input)", "6"),
        ("4.3", "  Simulación (POST /api/hydro/simulate)", "7"),
        ("4.4", "  Tendencia (GET /api/hydro/trend)", "9"),
        ("5.", "Modelo de Datos — Compatibilidad Spring Boot", "10"),
        ("6.", "Modelo Hidrológico", "11"),
        ("7.", "Niveles de Referencia", "11"),
        ("8.", "Códigos de Error", "12"),
        ("9.", "Ejemplos de Integración", "12"),
    ]

    toc_data = [["§", "Sección", "Pág."]]
    for num, title, page in toc_items:
        toc_data.append([num, title, page])

    t = make_table(toc_data, col_widths=[0.5 * inch, 4.5 * inch, 0.6 * inch])
    elements.append(t)
    elements.append(PageBreak())


def build_section_general(elements, styles):
    """Sección 1: Información General."""
    elements.append(Paragraph("1. Información General", styles["SectionTitle"]))
    elements.append(green_divider())

    info_data = [
        ["Campo", "Valor"],
        ["Base URL", "http://<servidor>:5215"],
        ["Formato de respuesta", "JSON (UTF-8)"],
        ["Modelo matemático", "HUI (Hidrograma Unitario Instantáneo) + SCS-NRCS"],
        ["Cascada de presas", "Angostura → Chicoasén → Malpaso → Juan Grijalva → Peñitas"],
        ["Horizonte máximo", "360 horas (15 días)"],
        ["Compatibilidad", "grijalva-hydro-model-service (Spring Boot)"],
    ]
    elements.append(make_table(info_data, col_widths=[2 * inch, 4.5 * inch]))
    elements.append(Spacer(1, 12))


def build_section_auth(elements, styles):
    """Sección 2: Autenticación y Roles."""
    elements.append(Paragraph("2. Autenticación y Roles", styles["SectionTitle"]))
    elements.append(green_divider())

    elements.append(Paragraph(
        "La API soporta dos métodos de autenticación. Cualquiera de los dos es suficiente "
        "para acceder a todos los endpoints:",
        styles["BodyText2"]
    ))
    elements.append(Spacer(1, 6))

    elements.append(Paragraph("Método 1: API Key (Header)", styles["SubSectionTitle"]))
    elements.append(Paragraph(
        "Enviar el header <b>X-Api-Key</b> con la clave asignada en cada petición.",
        styles["BodyText2"]
    ))
    elements.append(code_block(
        'curl -H "X-Api-Key: pih-grijalva-2026" http://servidor:5215/api/hydro/dams',
        styles
    ))

    elements.append(Paragraph("Método 2: JWT Bearer Token", styles["SubSectionTitle"]))
    elements.append(Paragraph(
        "Autenticarse con usuario/contraseña en el endpoint de login para obtener un token JWT. "
        "El usuario debe tener el rol <b>ApiConsumer</b>, <b>SuperAdmin</b> o <b>Administrador</b>.",
        styles["BodyText2"]
    ))
    elements.append(code_block(
        '# 1. Obtener token\n'
        'curl -X POST http://servidor:5215/api/auth/login \\\n'
        '  -H "Content-Type: application/json" \\\n'
        '  -d \'{"username": "api_user", "password": "SecurePass123"}\'\n'
        '\n'
        '# 2. Usar token\n'
        'curl -H "Authorization: Bearer eyJhbGciOi..." \\\n'
        '  http://servidor:5215/api/hydro/dams',
        styles
    ))
    elements.append(Spacer(1, 8))

    auth_data = [
        ["Método", "Header", "Ejemplo"],
        ["API Key", "X-Api-Key: <clave>", "X-Api-Key: pih-grijalva-2026"],
        ["JWT Bearer", "Authorization: Bearer <token>", "Authorization: Bearer eyJhbG..."],
    ]
    elements.append(make_table(auth_data, col_widths=[1.2 * inch, 2.3 * inch, 3 * inch]))
    elements.append(Spacer(1, 12))


def build_section_role(elements, styles):
    """Sección 3: Rol ApiConsumer."""
    elements.append(Paragraph("3. Rol ApiConsumer", styles["SectionTitle"]))
    elements.append(green_divider())

    elements.append(Paragraph(
        "Se ha creado el rol <b>ApiConsumer</b> específicamente para usuarios que consumen "
        "la API de pronóstico hidrológico sin acceso a la interfaz web de administración.",
        styles["BodyText2"]
    ))
    elements.append(Spacer(1, 6))

    roles_data = [
        ["Rol", "Acceso Web", "Acceso API", "Descripción"],
        ["SuperAdmin", "✓ Completo", "✓", "Control total del sistema"],
        ["Administrador", "✓ Gestión", "✓", "Administración de estaciones y usuarios"],
        ["Operador", "✓ Operativo", "✗", "Carga de datos y operación diaria"],
        ["Visualizador", "✓ Lectura", "✗", "Solo visualización de datos"],
        ["SoloVasos", "✓ FunVasos", "✗", "Acceso limitado a Funcionamiento de Vasos"],
        ["ApiConsumer", "✗", "✓", "Consumo exclusivo de API REST"],
    ]
    elements.append(make_table(roles_data, col_widths=[1.3 * inch, 1.1 * inch, 0.9 * inch, 3.2 * inch]))
    elements.append(Spacer(1, 8))

    elements.append(Paragraph("Creación de usuario ApiConsumer:", styles["SubSectionTitle"]))
    elements.append(Paragraph(
        "Un administrador puede crear usuarios con rol ApiConsumer desde el panel de "
        "administración: <b>Administración → Usuarios → Crear Usuario</b>, seleccionando "
        "el rol \"ApiConsumer\" en el desplegable.",
        styles["BodyText2"]
    ))
    elements.append(PageBreak())


def build_section_endpoints(elements, styles):
    """Sección 4: Endpoints."""
    elements.append(Paragraph("4. Endpoints", styles["SectionTitle"]))
    elements.append(green_divider())

    # ── 4.1 GET /api/hydro/dams ──
    elements.append(Paragraph("4.1 Catálogo de Presas", styles["SubSectionTitle"]))
    elements.append(code_block("GET /api/hydro/dams", styles))
    elements.append(Paragraph(
        "Devuelve las 5 presas del sistema con sus niveles de referencia. "
        "Formato compatible con <b>Dam.java</b> del microservicio Spring Boot.",
        styles["BodyText2"]
    ))

    dams_response = (
        '[\n'
        '  {\n'
        '    "id": 1,\n'
        '    "centralId": 1,\n'
        '    "code": "ANG",\n'
        '    "description": "Angostura",\n'
        '    "nameValue": 542.1,\n'
        '    "namoValue": 539.0,\n'
        '    "naminoValue": 510,\n'
        '    "hasPreviousDam": false,\n'
        '    "modelType": "HUI"\n'
        '  },\n'
        '  ...\n'
        ']'
    )
    elements.append(Paragraph("Respuesta:", styles["SmallNote"]))
    elements.append(code_block(dams_response, styles))

    dam_fields = [
        ["Campo", "Tipo", "Descripción"],
        ["id", "int", "Identificador único de la presa"],
        ["centralId", "int", "ID de la central hidroeléctrica"],
        ["code", "string", "Clave corta (ANG, CHI, MAL, JGR, PEN)"],
        ["description", "string", "Nombre completo de la presa"],
        ["nameValue", "float", "Nivel de Aguas Máximas Extraordinarias (msnm)"],
        ["namoValue", "float", "Nivel de Aguas Máximas Ordinarias (msnm)"],
        ["naminoValue", "int", "Nivel Mínimo de Operación (msnm)"],
        ["hasPreviousDam", "bool", "¿Tiene presa aguas arriba?"],
        ["modelType", "string", "Tipo de modelo hidrológico"],
    ]
    elements.append(make_table(dam_fields, col_widths=[1.5 * inch, 0.8 * inch, 4.2 * inch]))
    elements.append(Spacer(1, 12))

    # ── 4.2 GET /api/hydro/input ──
    elements.append(Paragraph("4.2 Datos de Entrada del Modelo", styles["SubSectionTitle"]))
    elements.append(code_block("GET /api/hydro/input?horizonHours=72", styles))
    elements.append(Paragraph(
        "Devuelve las condiciones iniciales reales (último dato de FunVasos) y el "
        "pronóstico de lluvia por cuenca que alimentan la simulación.",
        styles["BodyText2"]
    ))

    input_params = [
        ["Parámetro", "Tipo", "Default", "Rango", "Descripción"],
        ["horizonHours", "int", "72", "1–360", "Horas de pronóstico a consultar"],
    ]
    elements.append(make_table(input_params, col_widths=[1.3 * inch, 0.6 * inch, 0.7 * inch, 0.7 * inch, 3.2 * inch]))
    elements.append(Spacer(1, 12))

    # ── 4.3 POST /api/hydro/simulate ──
    elements.append(PageBreak())
    elements.append(Paragraph("4.3 Simulación Hidrológica", styles["SubSectionTitle"]))
    elements.append(code_block("POST /api/hydro/simulate", styles))
    elements.append(Paragraph(
        "Ejecuta el modelo hidrológico completo (balance hídrico hora a hora en cascada). "
        "La respuesta es un <b>array de HydroModel</b>, compatible con el formato del "
        "microservicio Spring Boot <b>grijalva-hydro-model-service</b>.",
        styles["BodyText2"]
    ))

    elements.append(Paragraph("Cuerpo de la petición (body):", styles["SmallNote"]))
    req_body = (
        '{\n'
        '  "horizonHours": 72,\n'
        '  "extractions": {\n'
        '    "Angostura": 120.5,\n'
        '    "Chicoasen": 200.0,\n'
        '    "Malpaso": 180.0,\n'
        '    "JGrijalva": 0,\n'
        '    "Penitas": 150.0\n'
        '  }\n'
        '}'
    )
    elements.append(code_block(req_body, styles))

    sim_params = [
        ["Campo", "Tipo", "Descripción"],
        ["horizonHours", "int", "Horas de pronóstico (1–360). Default: 72"],
        ["extractions", "dict", "Extracción constante por presa (m³/s). Opcional."],
        ["extractionSchedule", "dict", 'Extracción variable por día: {"Angostura": [120, 130, 125]}'],
        ["aportationSchedule", "dict", "Aportación manual por día (m³/s). Reemplaza la calculada."],
        ["drainCoefficients", "dict", "Coeficiente de escurrimiento por presa. Opcional."],
        ["curveNumbers", "dict", "Curve Number SCS por presa. Opcional."],
    ]
    elements.append(make_table(sim_params, col_widths=[1.7 * inch, 0.7 * inch, 4.1 * inch]))
    elements.append(Spacer(1, 8))

    elements.append(Paragraph("Respuesta — formato HydroModel (compatible Spring Boot):", styles["SmallNote"]))
    sim_response = (
        '[\n'
        '  {\n'
        '    "subBasinId": 1,\n'
        '    "modelType": "HUI",\n'
        '    "date": "2026-04-10",\n'
        '    "dam": {\n'
        '      "id": 1,\n'
        '      "centralId": 1,\n'
        '      "code": "ANG",\n'
        '      "description": "Angostura",\n'
        '      "nameValue": 542.1,\n'
        '      "namoValue": 539.0,\n'
        '      "naminoValue": 510,\n'
        '      "hasPreviousDam": false\n'
        '    },\n'
        '    "records": [\n'
        '      {\n'
        '        "date": "2026-04-10",\n'
        '        "rain": 2.50,\n'
        '        "extractionPreviousDam": 0.0000,\n'
        '        "elevation": 532.18,\n'
        '        "extraction": 0.0120,\n'
        '        "basinInput": 0.0150,\n'
        '        "totalCapacity": 12505.20,\n'
        '        "forecast": true,\n'
        '        "hour": 1\n'
        '      }\n'
        '    ]\n'
        '  }\n'
        ']'
    )
    elements.append(code_block(sim_response, styles))

    record_fields = [
        ["Campo", "Tipo", "Unidad", "Descripción"],
        ["date", "string", "ISO 8601", "Fecha del registro"],
        ["rain", "decimal", "mm", "Precipitación promedio de la subcuenca"],
        ["extractionPreviousDam", "decimal", "Mm³/h", "Aportación de presa aguas arriba"],
        ["elevation", "decimal", "msnm", "Nivel del embalse calculado"],
        ["extraction", "decimal", "Mm³/h", "Extracción total (turbinado + vertido)"],
        ["basinInput", "decimal", "Mm³/h", "Aportación por escurrimiento de cuenca propia"],
        ["totalCapacity", "decimal", "Mm³", "Almacenamiento total del embalse"],
        ["forecast", "boolean", "—", "true = pronóstico, false = dato real"],
        ["hour", "int", "—", "Hora secuencial desde el inicio (1, 2, ...)"],
    ]
    elements.append(Paragraph("Campos de cada registro (records):", styles["SmallNote"]))
    elements.append(make_table(record_fields, col_widths=[1.7 * inch, 0.7 * inch, 0.7 * inch, 3.4 * inch]))
    elements.append(Spacer(1, 12))

    # ── 4.4 GET /api/hydro/trend ──
    elements.append(PageBreak())
    elements.append(Paragraph("4.4 Tendencia (Datos Reales + Pronóstico)", styles["SubSectionTitle"]))
    elements.append(code_block("GET /api/hydro/trend?realDays=5&forecastHours=72", styles))
    elements.append(Paragraph(
        "Combina datos históricos reales de FunVasos con el pronóstico del modelo "
        "en un solo array de HydroModel. Los registros reales tienen <b>forecast=false</b> "
        "y los pronosticados <b>forecast=true</b>. Ideal para gráficas de tendencia.",
        styles["BodyText2"]
    ))

    trend_params = [
        ["Parámetro", "Tipo", "Default", "Rango", "Descripción"],
        ["realDays", "int", "5", "1–30", "Días de datos reales hacia atrás"],
        ["forecastHours", "int", "72", "1–360", "Horas de pronóstico hacia adelante"],
    ]
    elements.append(make_table(trend_params, col_widths=[1.3 * inch, 0.6 * inch, 0.7 * inch, 0.7 * inch, 3.2 * inch]))
    elements.append(Spacer(1, 8))

    elements.append(Paragraph(
        "La respuesta tiene el mismo formato que <b>/api/hydro/simulate</b>: un array de "
        "HydroModel con campo <b>records</b> que mezcla datos reales (forecast=false) "
        "y pronóstico (forecast=true) en orden cronológico.",
        styles["BodyText2"]
    ))
    elements.append(Spacer(1, 12))


def build_section_springboot(elements, styles):
    """Sección 5: Modelo de datos — Compatibilidad Spring Boot."""
    elements.append(Paragraph("5. Modelo de Datos — Compatibilidad Spring Boot", styles["SectionTitle"]))
    elements.append(green_divider())

    elements.append(Paragraph(
        "La API de CloudStation produce respuestas JSON idénticas al microservicio "
        "<b>grijalva-hydro-model-service</b> desarrollado en Spring Boot / Java. "
        "A continuación el mapeo de clases Java → JSON:",
        styles["BodyText2"]
    ))
    elements.append(Spacer(1, 6))

    mapping = [
        ["Clase Java", "Endpoint", "Estructura JSON"],
        ["HydroModel", "/api/hydro/simulate", "{ subBasinId, modelType, date, dam, records[] }"],
        ["HydroModelRecord", "(item en records)", "{ date, rain, extractionPreviousDam, elevation, ... }"],
        ["Dam", "/api/hydro/dams", "{ id, code, description, nameValue, namoValue, ... }"],
    ]
    elements.append(make_table(mapping, col_widths=[1.5 * inch, 1.8 * inch, 3.2 * inch]))
    elements.append(Spacer(1, 8))

    elements.append(Paragraph(
        "<b>Nota sobre serialización:</b> En Jackson (Java), el campo booleano "
        "\"isForecast\" se serializa como \"forecast\" (el getter <i>isForecast()</i> "
        "omite el prefijo \"is\"). Esta API replica ese comportamiento exacto.",
        styles["SmallNote"]
    ))
    elements.append(Spacer(1, 6))

    elements.append(Paragraph(
        "<b>Nota sobre tipos numéricos:</b> Los campos numéricos en Java usan BigDecimal "
        "para precisión. En esta API los valores se redondean a 2 decimales (niveles, "
        "almacenamiento) o 4 decimales (volúmenes horarios).",
        styles["SmallNote"]
    ))
    elements.append(Spacer(1, 12))


def build_section_model(elements, styles):
    """Sección 6: Modelo Hidrológico."""
    elements.append(Paragraph("6. Modelo Hidrológico", styles["SectionTitle"]))
    elements.append(green_divider())

    elements.append(Paragraph(
        "El pronóstico utiliza un <b>modelo concentrado</b> con los siguientes componentes:",
        styles["BodyText2"]
    ))
    elements.append(Spacer(1, 4))

    model_steps = [
        "<b>Lluvia Efectiva (SCS-NRCS):</b> PE = (P − 0.2S)² / (P + 0.8S), donde S = 25400/CN − 254",
        "<b>Convolución HUI:</b> El escurrimiento se calcula como la convolución de PE con el Hidrograma Unitario Instantáneo de la subcuenca.",
        "<b>Balance Hídrico:</b> V(t) = V(t−1) + Q_cuenca + Q_upstream − Q_extracción",
        "<b>Cascada:</b> Las extracciones de una presa llegan como aportación a la siguiente (con desfase temporal configurable).",
    ]
    for step in model_steps:
        elements.append(Paragraph(f"• {step}", styles["BulletItem"]))

    elements.append(Spacer(1, 12))


def build_section_levels(elements, styles):
    """Sección 7: Niveles de Referencia."""
    elements.append(Paragraph("7. Niveles de Referencia", styles["SectionTitle"]))
    elements.append(green_divider())

    levels_data = [
        ["Presa", "Código", "NAMO (msnm)", "NAME (msnm)", "NAMINO (msnm)"],
        ["Angostura", "ANG", "539.00", "542.10", "510"],
        ["Chicoasén", "CHI", "395.00", "400.00", "378"],
        ["Malpaso", "MAL", "189.70", "192.00", "163"],
        ["Tapón Juan Grijalva", "JGR", "100.00", "105.50", "87"],
        ["Peñitas", "PEN", "95.10", "99.20", "84"],
    ]
    elements.append(make_table(levels_data, col_widths=[1.8 * inch, 0.7 * inch, 1.2 * inch, 1.2 * inch, 1.2 * inch]))
    elements.append(Spacer(1, 8))

    level_defs = [
        ["Nivel", "Significado", "Uso"],
        ["NAMO", "Nivel de Aguas Máximas Ordinarias", "Operación normal. Referencia de llenado."],
        ["NAME", "Nivel de Aguas Máximas Extraordinarias", "Emergencia. Nunca debe rebasarse."],
        ["NAMINO", "Nivel Mínimo de Operación", "Límite inferior para generación."],
    ]
    elements.append(make_table(level_defs, col_widths=[1 * inch, 2.5 * inch, 3 * inch]))
    elements.append(Spacer(1, 12))


def build_section_errors(elements, styles):
    """Sección 8: Códigos de Error."""
    elements.append(Paragraph("8. Códigos de Error", styles["SectionTitle"]))
    elements.append(green_divider())

    errors_data = [
        ["HTTP", "Significado", "Cuándo ocurre"],
        ["401", "No autorizado", "API key inválida/faltante y sin JWT válido con rol autorizado"],
        ["400", "Petición incorrecta", "Parámetros fuera de rango o body mal formado"],
        ["500", "Error interno", "Falla en base de datos o error de cálculo del modelo"],
    ]
    elements.append(make_table(errors_data, col_widths=[0.7 * inch, 1.5 * inch, 4.3 * inch]))
    elements.append(Spacer(1, 6))

    elements.append(Paragraph("Formato de error:", styles["SmallNote"]))
    elements.append(code_block('{ "error": "API key inválida o usuario no autorizado" }', styles))
    elements.append(Spacer(1, 12))


def build_section_examples(elements, styles):
    """Sección 9: Ejemplos de Integración."""
    elements.append(PageBreak())
    elements.append(Paragraph("9. Ejemplos de Integración", styles["SectionTitle"]))
    elements.append(green_divider())

    # Python example
    elements.append(Paragraph("9.1 Python", styles["SubSectionTitle"]))
    python_code = (
        'import requests\n'
        '\n'
        'BASE = "http://atlas16.ddns.net:5215"\n'
        'HEADERS = {"X-Api-Key": "pih-grijalva-2026"}\n'
        '\n'
        '# Obtener tendencia (5 días reales + 3 días pronóstico)\n'
        'resp = requests.get(f"{BASE}/api/hydro/trend",\n'
        '                    headers=HEADERS,\n'
        '                    params={"realDays": 5, "forecastHours": 72})\n'
        'models = resp.json()  # Array de HydroModel\n'
        '\n'
        'for model in models:\n'
        '    dam = model["dam"]\n'
        '    print(f"\\n=== {dam[\'description\']} ({dam[\'code\']}) ===")\n'
        '    print(f"  NAMO: {dam[\'namoValue\']} | NAME: {dam[\'nameValue\']}")\n'
        '\n'
        '    reales = [r for r in model["records"] if not r["forecast"]]\n'
        '    pronostico = [r for r in model["records"] if r["forecast"]]\n'
        '    print(f"  Reales: {len(reales)}, Pronóstico: {len(pronostico)}")\n'
        '\n'
        '# Ejecutar simulación con extracciones personalizadas\n'
        'sim = requests.post(f"{BASE}/api/hydro/simulate",\n'
        '    headers={**HEADERS, "Content-Type": "application/json"},\n'
        '    json={"horizonHours": 72, "extractions": {\n'
        '        "Angostura": 120, "Chicoasen": 200,\n'
        '        "Malpaso": 180, "Penitas": 150\n'
        '    }})\n'
        'for model in sim.json():\n'
        '    last = model["records"][-1]\n'
        '    print(f"{model[\"dam\"][\"code\"]}: elev={last[\"elevation\"]}")'
    )
    elements.append(code_block(python_code, styles))

    # C# example
    elements.append(Paragraph("9.2 C# (.NET)", styles["SubSectionTitle"]))
    csharp_code = (
        'using var http = new HttpClient();\n'
        'http.DefaultRequestHeaders.Add("X-Api-Key", "pih-grijalva-2026");\n'
        '\n'
        '// Simulación con extracciones personalizadas\n'
        'var request = new {\n'
        '    horizonHours = 72,\n'
        '    extractions = new Dictionary<string, double> {\n'
        '        ["Angostura"] = 120, ["Chicoasen"] = 200,\n'
        '        ["Malpaso"] = 180, ["Penitas"] = 150\n'
        '    }\n'
        '};\n'
        '\n'
        'var json = JsonSerializer.Serialize(request);\n'
        'var content = new StringContent(json, Encoding.UTF8,\n'
        '    "application/json");\n'
        'var resp = await http.PostAsync(\n'
        '    "http://servidor:5215/api/hydro/simulate", content);\n'
        'var result = await resp.Content.ReadAsStringAsync();\n'
        'Console.WriteLine(result);'
    )
    elements.append(code_block(csharp_code, styles))

    # cURL example
    elements.append(Paragraph("9.3 cURL", styles["SubSectionTitle"]))
    curl_code = (
        '# Catálogo de presas\n'
        'curl -H "X-Api-Key: pih-grijalva-2026" \\\n'
        '  http://servidor:5215/api/hydro/dams\n'
        '\n'
        '# Simulación (body mínimo)\n'
        'curl -X POST \\\n'
        '  -H "X-Api-Key: pih-grijalva-2026" \\\n'
        '  -H "Content-Type: application/json" \\\n'
        '  -d \'{"horizonHours": 72}\' \\\n'
        '  http://servidor:5215/api/hydro/simulate\n'
        '\n'
        '# Tendencia con JWT\n'
        'curl -H "Authorization: Bearer eyJhbG..." \\\n'
        '  "http://servidor:5215/api/hydro/trend?realDays=7&forecastHours=120"'
    )
    elements.append(code_block(curl_code, styles))
    elements.append(Spacer(1, 12))


def main():
    doc = SimpleDocTemplate(
        OUTPUT_FILE,
        pagesize=letter,
        leftMargin=0.75 * inch,
        rightMargin=0.75 * inch,
        topMargin=inch,
        bottomMargin=0.75 * inch,
        title="API de Pronóstico Hidrológico — CFE",
        author="Subgerencia Técnica · Hidroeléctrica Grijalva",
        subject="Documentación técnica de la API REST",
    )

    styles = build_styles()
    elements = []

    build_cover(elements, styles)
    build_toc(elements, styles)
    build_section_general(elements, styles)
    build_section_auth(elements, styles)
    build_section_role(elements, styles)
    build_section_endpoints(elements, styles)
    build_section_springboot(elements, styles)
    build_section_model(elements, styles)
    build_section_levels(elements, styles)
    build_section_errors(elements, styles)
    build_section_examples(elements, styles)

    # Final page
    elements.append(PageBreak())
    elements.append(Spacer(1, 2 * inch))
    final_style = ParagraphStyle("final", fontSize=12, leading=16, textColor=CFE_GREEN,
                                  alignment=TA_CENTER, fontName="Helvetica-Bold")
    elements.append(Paragraph("— Fin del Documento —", final_style))
    elements.append(Spacer(1, 12))
    elements.append(Paragraph(
        "Comisión Federal de Electricidad<br/>"
        "Subgerencia Técnica · Hidroeléctrica Grijalva<br/>"
        "Sistema CloudStation — Pronóstico Hidrológico",
        ParagraphStyle("finalOrg", fontSize=10, leading=14, textColor=CFE_TEXT, alignment=TA_CENTER)
    ))

    doc.build(elements, onFirstPage=header_footer, onLaterPages=header_footer)
    print(f"\n✅ PDF generado exitosamente: {OUTPUT_FILE}")
    print(f"   Tamaño: {os.path.getsize(OUTPUT_FILE) / 1024:.1f} KB")


if __name__ == "__main__":
    main()
