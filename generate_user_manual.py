#!/usr/bin/env python3
"""
Genera el Manual de Usuario de la Plataforma Integral Hidrometeorológica (PIH)
en formato PDF, orientado a usuarios finales (para dummies).
"""

from fpdf import FPDF
import os

class ManualPDF(FPDF):
    """PDF personalizado con encabezado/pie CFE."""

    def __init__(self):
        super().__init__('P', 'mm', 'Letter')
        self.chapter_num = 0
        self.section_num = 0
        # Register Unicode TTF fonts
        font_dir = '/System/Library/Fonts/Supplemental/'
        self.add_font('Arial', '', font_dir + 'Arial.ttf', uni=True)
        self.add_font('Arial', 'B', font_dir + 'Arial Bold.ttf', uni=True)
        self.add_font('Arial', 'I', font_dir + 'Arial Italic.ttf', uni=True)
        self.add_font('Arial', 'BI', font_dir + 'Arial Bold Italic.ttf', uni=True)

    # ── Encabezado ─────────────────────────────────────────────
    def header(self):
        if self.page_no() == 1:
            return  # La portada no tiene encabezado
        self.set_font('Arial', 'I', 8)
        self.set_text_color(100, 100, 100)
        self.cell(0, 8, 'PIH - Manual de Usuario  |  CFE - Subgerencia Regional de Generación Hidroeléctrica Grijalva', 0, 0, 'L')
        self.cell(0, 8, f'Página {self.page_no()}', 0, 1, 'R')
        self.set_draw_color(0, 128, 64)
        self.line(10, 14, self.w - 10, 14)
        self.ln(4)

    # ── Pie de página ──────────────────────────────────────────
    def footer(self):
        if self.page_no() == 1:
            return
        self.set_y(-15)
        self.set_font('Arial', 'I', 7)
        self.set_text_color(130, 130, 130)
        self.cell(0, 10, '© 2026 CFE - Plataforma Integral Hidrometeorológica - Todos los derechos reservados', 0, 0, 'C')

    # ── Portada ────────────────────────────────────────────────
    def cover_page(self):
        self.add_page()
        self.ln(40)
        # Logo placeholder (cuadro verde)
        self.set_fill_color(0, 128, 64)
        self.rect(self.w/2 - 25, 50, 50, 50, 'F')
        self.set_font('Arial', 'B', 28)
        self.set_text_color(255, 255, 255)
        self.set_xy(self.w/2 - 25, 62)
        self.cell(50, 16, 'PIH', 0, 0, 'C')
        self.set_xy(self.w/2 - 25, 78)
        self.set_font('Arial', '', 9)
        self.cell(50, 8, 'CloudStation', 0, 0, 'C')

        self.ln(70)
        self.set_text_color(30, 30, 30)
        self.set_font('Arial', 'B', 32)
        self.cell(0, 16, 'Manual de Usuario', 0, 1, 'C')
        self.set_font('Arial', '', 16)
        self.set_text_color(80, 80, 80)
        self.cell(0, 10, 'Plataforma Integral Hidrometeorológica', 0, 1, 'C')
        self.ln(8)
        self.set_draw_color(0, 128, 64)
        self.line(self.w/2 - 40, self.get_y(), self.w/2 + 40, self.get_y())
        self.ln(10)
        self.set_font('Arial', '', 12)
        self.set_text_color(100, 100, 100)
        self.cell(0, 8, 'Comisión Federal de Electricidad', 0, 1, 'C')
        self.cell(0, 8, 'Subgerencia Regional de Generación Hidroeléctrica Grijalva', 0, 1, 'C')
        self.ln(6)
        self.set_font('Arial', 'I', 11)
        self.cell(0, 8, 'Versión 2.0  —  Marzo 2026', 0, 1, 'C')
        self.ln(20)
        self.set_font('Arial', '', 10)
        self.set_text_color(60, 60, 60)
        self.cell(0, 7, 'Guía completa para usuarios de todos los niveles', 0, 1, 'C')
        self.cell(0, 7, 'Incluye: Mapa, Reportes, Análisis, Pronósticos, Chat y Administración', 0, 1, 'C')

    # ── Índice ─────────────────────────────────────────────────
    def table_of_contents(self):
        self.add_page()
        self.set_font('Arial', 'B', 22)
        self.set_text_color(0, 100, 50)
        self.cell(0, 14, 'Índice de Contenido', 0, 1, 'L')
        self.ln(4)
        self.set_draw_color(0, 128, 64)
        self.line(10, self.get_y(), self.w - 10, self.get_y())
        self.ln(8)

        chapters = [
            ("1", "Introducción", [
                "1.1 ¿Qué es la PIH?",
                "1.2 Requisitos del sistema",
                "1.3 Roles de usuario",
            ]),
            ("2", "Inicio de Sesión", [
                "2.1 Cómo iniciar sesión",
                "2.2 Recuperar contraseña",
                "2.3 Cerrar sesión",
            ]),
            ("3", "Mapa Interactivo", [
                "3.1 Navegación del mapa",
                "3.2 Seleccionar variable",
                "3.3 Ver datos de una estación",
                "3.4 Capas: cuencas y subcuencas",
                "3.5 Semáforo de precipitación",
            ]),
            ("4", "Cortes Horarios", [
                "4.1 Seleccionar variable y fecha",
                "4.2 Leer la tabla de datos",
                "4.3 Exportar a Excel",
            ]),
            ("5", "Mis Grupos de Estaciones", [
                "5.1 Crear un grupo",
                "5.2 Agregar estaciones",
                "5.3 Usar grupos en reportes",
            ]),
            ("6", "Análisis de Datos", [
                "6.1 Seleccionar estaciones y rango",
                "6.2 Tipos de gráficas",
                "6.3 Exportar datos",
            ]),
            ("7", "Monitoreo GOES", [
                "7.1 Estado de las transmisiones",
                "7.2 Tabla de decodificación",
                "7.3 Indicadores de salud",
            ]),
            ("8", "Repositorio de Documentos", [
                "8.1 Ver documentos disponibles",
                "8.2 Subir un documento",
                "8.3 Historial y calendario",
                "8.4 Bitácora de auditoría",
            ]),
            ("9", "Funcionamiento de Vasos", [
                "9.1 Tabla de datos por presa",
                "9.2 Gráfica elevación-almacenamiento",
                "9.3 Líneas de referencia",
            ]),
            ("10", "Pronóstico de Lluvia", [
                "10.1 Resumen por cuenca",
                "10.2 Gráficas de precipitación",
                "10.3 Tabla de pronóstico detallado",
            ]),
            ("11", "Chat en Tiempo Real", [
                "11.1 Salas de conversación",
                "11.2 Enviar y recibir mensajes",
                "11.3 Usuarios en línea",
            ]),
            ("12", "Administración (solo Administradores)", [
                "12.1 Gestión de Estaciones",
                "12.2 Órdenes de Mantenimiento",
                "12.3 Cuencas y Subcuencas",
                "12.4 Gestión de Usuarios",
                "12.5 Centro de Notificaciones",
                "12.6 Sistema de Alertas Tempranas",
            ]),
        ]

        for ch_num, ch_title, sections in chapters:
            self.set_font('Arial', 'B', 12)
            self.set_text_color(30, 30, 30)
            self.cell(0, 8, f'{ch_num}.  {ch_title}', 0, 1)
            for sec in sections:
                self.set_font('Arial', '', 10)
                self.set_text_color(80, 80, 80)
                self.cell(12)
                self.cell(0, 6, sec, 0, 1)
            self.ln(2)

    # ── Helpers ────────────────────────────────────────────────
    def chapter_title(self, title):
        self.chapter_num += 1
        self.section_num = 0
        self.add_page()
        self.set_fill_color(0, 100, 50)
        self.rect(10, 10, self.w - 20, 32, 'F')
        self.set_font('Arial', 'B', 24)
        self.set_text_color(255, 255, 255)
        self.set_xy(18, 14)
        self.cell(0, 12, f'{self.chapter_num}', 0, 0, 'L')
        self.set_xy(18, 26)
        self.set_font('Arial', 'B', 16)
        self.cell(0, 10, title, 0, 0, 'L')
        self.set_text_color(30, 30, 30)
        self.ln(36)

    def section_title(self, title):
        self.section_num += 1
        self.ln(4)
        self.set_font('Arial', 'B', 13)
        self.set_text_color(0, 100, 50)
        num = f'{self.chapter_num}.{self.section_num}'
        self.cell(0, 9, f'{num}  {title}', 0, 1, 'L')
        self.set_draw_color(0, 180, 90)
        self.line(10, self.get_y(), 90, self.get_y())
        self.ln(4)

    def body_text(self, text):
        self.set_font('Arial', '', 10.5)
        self.set_text_color(50, 50, 50)
        self.multi_cell(0, 6, text)
        self.ln(2)

    def tip_box(self, text):
        self.ln(2)
        y = self.get_y()
        self.set_fill_color(230, 245, 235)
        self.set_draw_color(0, 160, 80)
        self.rect(14, y, self.w - 28, 6 * (len(text) // 80 + 2) + 4, 'FD')
        self.set_xy(18, y + 3)
        self.set_font('Arial', 'B', 9)
        self.set_text_color(0, 100, 50)
        self.cell(0, 5, 'CONSEJO', 0, 1)
        self.set_x(18)
        self.set_font('Arial', '', 9)
        self.set_text_color(40, 80, 40)
        self.multi_cell(self.w - 36, 5, text)
        self.ln(4)

    def warning_box(self, text):
        self.ln(2)
        y = self.get_y()
        self.set_fill_color(255, 245, 230)
        self.set_draw_color(200, 140, 0)
        self.rect(14, y, self.w - 28, 6 * (len(text) // 80 + 2) + 4, 'FD')
        self.set_xy(18, y + 3)
        self.set_font('Arial', 'B', 9)
        self.set_text_color(180, 100, 0)
        self.cell(0, 5, 'IMPORTANTE', 0, 1)
        self.set_x(18)
        self.set_font('Arial', '', 9)
        self.set_text_color(120, 80, 0)
        self.multi_cell(self.w - 36, 5, text)
        self.ln(4)

    def steps(self, step_list):
        """Imprime una lista numerada de pasos."""
        for i, step in enumerate(step_list, 1):
            self.set_font('Arial', 'B', 10.5)
            self.set_text_color(0, 100, 50)
            self.cell(8, 6, f'{i}.', 0, 0)
            self.set_font('Arial', '', 10.5)
            self.set_text_color(50, 50, 50)
            self.multi_cell(0, 6, step)
            self.ln(1)
        self.ln(2)

    def bullet_list(self, items):
        for item in items:
            self.set_font('Arial', '', 10.5)
            self.set_text_color(50, 50, 50)
            self.cell(6, 6, chr(8226), 0, 0)
            self.multi_cell(0, 6, f' {item}')
            self.ln(1)
        self.ln(2)

    def key_value_table(self, headers, rows):
        """Dibuja una tabla simple."""
        self.set_font('Arial', 'B', 9)
        self.set_fill_color(0, 100, 50)
        self.set_text_color(255, 255, 255)
        col_w = (self.w - 20) / len(headers)
        for h in headers:
            self.cell(col_w, 8, h, 1, 0, 'C', True)
        self.ln()

        self.set_font('Arial', '', 9)
        self.set_text_color(50, 50, 50)
        fill = False
        for row in rows:
            if self.get_y() > 250:
                self.add_page()
            if fill:
                self.set_fill_color(240, 248, 242)
            else:
                self.set_fill_color(255, 255, 255)
            for val in row:
                self.cell(col_w, 7, str(val), 1, 0, 'L', True)
            self.ln()
            fill = not fill
        self.ln(4)


def build_manual():
    pdf = ManualPDF()
    pdf.set_auto_page_break(auto=True, margin=20)

    # ═══════════════════════════════════════════════════════════
    # PORTADA E ÍNDICE
    # ═══════════════════════════════════════════════════════════
    pdf.cover_page()
    pdf.table_of_contents()

    # ═══════════════════════════════════════════════════════════
    # CAPÍTULO 1: INTRODUCCIÓN
    # ═══════════════════════════════════════════════════════════
    pdf.chapter_title('Introducción')

    pdf.section_title('¿Qué es la PIH?')
    pdf.body_text(
        'La Plataforma Integral Hidrometeorológica (PIH) es un sistema web desarrollado '
        'por la CFE para la Subgerencia Regional de Generación Hidroeléctrica Grijalva. '
        'Permite monitorear en tiempo real las condiciones hidrometeorológicas de las '
        'presas del sistema Grijalva: Angostura, Chicoasén, Malpaso y Peñitas.'
    )
    pdf.body_text(
        'Con la PIH puedes:'
    )
    pdf.bullet_list([
        'Ver un mapa interactivo con las estaciones de medición y sus lecturas actuales.',
        'Consultar reportes horarios de precipitación, nivel, temperatura y más.',
        'Analizar datos históricos con gráficas avanzadas.',
        'Monitorear las transmisiones del satélite GOES.',
        'Consultar el funcionamiento de vasos (presas) con datos actualizados.',
        'Ver pronósticos de lluvia por cuenca.',
        'Comunicarte con tu equipo mediante el chat integrado.',
        'Recibir alertas automáticas cuando se superan umbrales críticos.',
        'Gestionar documentos operativos (boletines, reportes diarios).',
    ])

    pdf.section_title('Requisitos del sistema')
    pdf.body_text('Para usar la PIH solo necesitas:')
    pdf.bullet_list([
        'Un navegador web moderno (Google Chrome, Microsoft Edge, Firefox o Safari).',
        'Conexión a la red de CFE o internet (según la configuración de tu oficina).',
        'Un usuario y contraseña proporcionados por el administrador del sistema.',
    ])
    pdf.tip_box('Se recomienda usar Google Chrome o Microsoft Edge para la mejor experiencia. Mantén tu navegador actualizado.')

    pdf.section_title('Roles de usuario')
    pdf.body_text(
        'La PIH tiene tres niveles de acceso. Tu administrador te asignará el rol adecuado:'
    )
    pdf.key_value_table(
        ['Rol', 'Puede ver datos', 'Puede subir docs', 'Puede administrar'],
        [
            ['Visualizador', 'Sí', 'No', 'No'],
            ['Operador', 'Sí', 'Sí', 'No'],
            ['Administrador', 'Sí', 'Sí', 'Sí (todo)'],
        ]
    )
    pdf.body_text(
        'Visualizador: Puede consultar mapas, reportes, gráficas y pronósticos. '
        'Operador: Además puede subir documentos y registrar bitácoras de mantenimiento. '
        'Administrador: Tiene acceso completo, incluyendo gestión de estaciones, '
        'usuarios, notificaciones y alertas tempranas.'
    )

    # ═══════════════════════════════════════════════════════════
    # CAPÍTULO 2: INICIO DE SESIÓN
    # ═══════════════════════════════════════════════════════════
    pdf.chapter_title('Inicio de Sesión')

    pdf.section_title('Cómo iniciar sesión')
    pdf.body_text(
        'Al abrir la PIH en tu navegador, verás la pantalla de inicio de sesión '
        'con el logo de PIH sobre un fondo oscuro elegante.'
    )
    pdf.steps([
        'Abre tu navegador y escribe la dirección de la PIH (ej: https://hidrometria.mx).',
        'En el campo "Usuario", escribe tu nombre de usuario.',
        'En el campo "Contraseña", escribe tu contraseña.',
        'Si deseas que el sistema recuerde tu sesión, marca la casilla "Recordarme".',
        'Haz clic en el botón "Iniciar Sesión".',
    ])
    pdf.warning_box('Después de 5 intentos fallidos, tu cuenta se bloqueará por 15 minutos como medida de seguridad. Si olvidaste tu contraseña, usa la opción "¿Olvidaste tu contraseña?".')

    pdf.section_title('Recuperar contraseña')
    pdf.steps([
        'En la pantalla de inicio de sesión, haz clic en "¿Olvidaste tu contraseña?".',
        'Escribe tu correo electrónico registrado en el sistema.',
        'Haz clic en "Enviar enlace de recuperación".',
        'Revisa tu correo electrónico (también la carpeta de spam).',
        'Haz clic en el enlace del correo y establece una nueva contraseña.',
    ])
    pdf.tip_box('Tu nueva contraseña debe tener al menos 6 caracteres, incluyendo una mayúscula, una minúscula y un número.')

    pdf.section_title('Cerrar sesión')
    pdf.steps([
        'Haz clic en tu nombre de usuario en la esquina superior derecha del menú.',
        'Se desplegará un menú con tu rol actual.',
        'Haz clic en "Cerrar Sesión" (icono rojo de salida).',
        'El sistema te llevará de vuelta a la pantalla de inicio de sesión.',
    ])

    # ═══════════════════════════════════════════════════════════
    # CAPÍTULO 3: MAPA INTERACTIVO
    # ═══════════════════════════════════════════════════════════
    pdf.chapter_title('Mapa Interactivo')

    pdf.section_title('Navegación del mapa')
    pdf.body_text(
        'El Mapa es la pantalla principal de la PIH. Muestra todas las estaciones '
        'hidrométricas y climatológicas del sistema Grijalva sobre un mapa interactivo.'
    )
    pdf.body_text('Controles básicos del mapa:')
    pdf.bullet_list([
        'Zoom: Usa la rueda del ratón o los botones + / - en el mapa.',
        'Mover: Haz clic y arrastra para desplazarte por el mapa.',
        'Estaciones: Cada punto de color representa una estación. El color indica el valor de la variable seleccionada.',
        'Presas: Los triángulos representan las presas del sistema.',
    ])

    pdf.section_title('Seleccionar variable')
    pdf.body_text(
        'En la parte superior del mapa encontrarás un selector de variable. '
        'Puedes elegir entre:'
    )
    pdf.bullet_list([
        'Precipitación (mm) — lluvia acumulada.',
        'Nivel (m) — nivel del agua en ríos y presas.',
        'Temperatura (°C) — temperatura ambiente.',
        'Humedad (%) — humedad relativa.',
        'Y otras variables disponibles según las estaciones.',
    ])
    pdf.body_text(
        'Al cambiar la variable, todos los marcadores del mapa se actualizan '
        'automáticamente con los colores correspondientes a la nueva variable.'
    )

    pdf.section_title('Ver datos de una estación')
    pdf.steps([
        'Haz clic en cualquier marcador (punto o triángulo) del mapa.',
        'Se abrirá un panel emergente con el nombre de la estación y sus datos actuales.',
        'Verás una mini gráfica con el historial de las últimas horas.',
        'Puedes cambiar el rango de horas (6h, 12h, 24h) en los botones del panel.',
    ])
    pdf.tip_box('Si una estación aparece con un ícono de herramienta, significa que está en mantenimiento y sus datos podrían no ser confiables temporalmente.')

    pdf.section_title('Capas: cuencas y subcuencas')
    pdf.body_text(
        'El mapa puede mostrar los límites geográficos de las cuencas y subcuencas '
        'usando archivos KML. Busca el control de capas en la esquina del mapa para '
        'activar o desactivar la visualización de cuencas.'
    )

    pdf.section_title('Semáforo de precipitación')
    pdf.body_text(
        'En el panel lateral o superior verás un resumen tipo semáforo que indica '
        'el estado de precipitación por cuenca:'
    )
    pdf.bullet_list([
        'Verde: Precipitación normal o sin lluvia.',
        'Amarillo: Precipitación moderada, atención.',
        'Naranja: Precipitación alta, precaución.',
        'Rojo: Precipitación muy alta, alerta.',
    ])

    # ═══════════════════════════════════════════════════════════
    # CAPÍTULO 4: CORTES HORARIOS
    # ═══════════════════════════════════════════════════════════
    pdf.chapter_title('Cortes Horarios')

    pdf.section_title('Seleccionar variable y fecha')
    pdf.body_text(
        'El módulo de Cortes Horarios muestra una tabla con los valores registrados '
        'hora por hora para todas las estaciones activas.'
    )
    pdf.steps([
        'En el menú superior, haz clic en "Cortes Horarios" (ícono de reloj).',
        'En la barra de controles, selecciona la variable que deseas consultar (ej: Precipitación).',
        'Elige la hora de inicio del corte (por defecto es 6:00 AM).',
        'Opcionalmente, selecciona una fecha específica. Por defecto se muestra el día actual.',
        'Si tienes grupos de estaciones creados, puedes filtrar por grupo.',
        'La tabla se actualizará automáticamente.',
    ])

    pdf.section_title('Leer la tabla de datos')
    pdf.body_text(
        'La tabla muestra:'
    )
    pdf.bullet_list([
        'Filas: Una fila por cada estación.',
        'Columnas: Una columna por cada hora del día.',
        'Valores: El valor medido en cada hora para la variable seleccionada.',
        'Total/Acumulado: Al final de cada fila se muestra el acumulado del día.',
        'ColorRANKing: Las celdas se colorean según la intensidad del valor.',
    ])
    pdf.tip_box('Puedes hacer clic en el nombre de una estación para ver sus datos detallados.')

    pdf.section_title('Exportar a Excel')
    pdf.steps([
        'Configura la tabla con la variable, fecha y grupo deseados.',
        'Haz clic en el botón "Exportar Excel" (ícono de descarga verde).',
        'Se descargará un archivo .xlsx con todos los datos de la tabla.',
        'Abre el archivo con Microsoft Excel, LibreOffice Calc o Google Sheets.',
    ])

    # ═══════════════════════════════════════════════════════════
    # CAPÍTULO 5: MIS GRUPOS DE ESTACIONES
    # ═══════════════════════════════════════════════════════════
    pdf.chapter_title('Mis Grupos de Estaciones')

    pdf.section_title('Crear un grupo')
    pdf.body_text(
        'Los grupos te permiten organizar las estaciones que más te interesan '
        'para consultarlas rápidamente en los reportes.'
    )
    pdf.steps([
        'En el menú, haz clic en "Mis Grupos" (ícono de personas).',
        'En el panel izquierdo, haz clic en el botón verde "+" (Nuevo Grupo).',
        'Escribe un nombre para tu grupo (ej: "Presas principales", "Cuenca Angostura").',
        'Haz clic en "Crear".',
    ])

    pdf.section_title('Agregar estaciones')
    pdf.steps([
        'Selecciona un grupo de la lista del panel izquierdo.',
        'En el panel derecho aparecerá la lista de todas las estaciones disponibles.',
        'Usa la barra de búsqueda para encontrar una estación por nombre o ID.',
        'Haz clic en la estación para agregarla al grupo.',
        'Para quitar una estación del grupo, haz clic en la "X" junto a su nombre.',
    ])

    pdf.section_title('Usar grupos en reportes')
    pdf.body_text(
        'Una vez creados tus grupos, puedes usarlos como filtro en:'
    )
    pdf.bullet_list([
        'Cortes Horarios: Selecciona tu grupo en el dropdown "Grupo" para ver solo esas estaciones.',
        'Análisis de Datos: Filtra por grupo para comparar estaciones específicas.',
    ])
    pdf.tip_box('Cada usuario tiene sus propios grupos privados. Otros usuarios no pueden ver ni modificar tus grupos.')

    # ═══════════════════════════════════════════════════════════
    # CAPÍTULO 6: ANÁLISIS DE DATOS
    # ═══════════════════════════════════════════════════════════
    pdf.chapter_title('Análisis de Datos')

    pdf.section_title('Seleccionar estaciones y rango')
    pdf.body_text(
        'El módulo de Análisis de Datos permite crear gráficas personalizadas '
        'con los datos históricos de las estaciones.'
    )
    pdf.steps([
        'En el menú, haz clic en "Análisis" (ícono de gráfica).',
        'En el panel de controles, selecciona una o más estaciones.',
        'Elige la variable a analizar (Precipitación, Nivel, etc.).',
        'Define el rango de fechas (fecha inicio y fecha fin).',
        'Haz clic en "Consultar" para generar la gráfica.',
    ])

    pdf.section_title('Tipos de gráficas')
    pdf.body_text(
        'El módulo genera gráficas interactivas usando Highcharts. Puedes:'
    )
    pdf.bullet_list([
        'Hacer zoom: Selecciona un área de la gráfica arrastrando el ratón.',
        'Ver valores exactos: Pasa el cursor sobre un punto para ver el tooltip.',
        'Ocultar series: Haz clic en el nombre de una serie en la leyenda para ocultarla.',
        'Restablecer zoom: Haz clic en "Reset zoom" para ver toda la gráfica.',
    ])

    pdf.section_title('Exportar datos')
    pdf.body_text(
        'Desde la gráfica de análisis puedes exportar los datos consultados. '
        'Busca el botón de exportación para descargar los datos en formato Excel.'
    )

    # ═══════════════════════════════════════════════════════════
    # CAPÍTULO 7: MONITOREO GOES
    # ═══════════════════════════════════════════════════════════
    pdf.chapter_title('Monitoreo GOES')

    pdf.section_title('Estado de las transmisiones')
    pdf.body_text(
        'El módulo GOES muestra el estado de las transmisiones satelitales GOES '
        '(Geostationary Operational Environmental Satellite) de todas las estaciones.'
    )
    pdf.body_text(
        'En la pantalla principal verás tarjetas de estadísticas:'
    )
    pdf.bullet_list([
        'Total de estaciones GOES registradas.',
        'Estaciones que transmitieron en las últimas horas.',
        'Estaciones con transmisión atrasada o sin datos.',
        'Porcentaje de cobertura actual.',
    ])

    pdf.section_title('Tabla de decodificación')
    pdf.body_text(
        'La tabla inferior muestra para cada estación GOES:'
    )
    pdf.bullet_list([
        'ID Satelital: Identificador único del transmisor GOES.',
        'Nombre de la estación.',
        'Última transmisión: Fecha y hora del último dato recibido.',
        'Estado: Indicador visual (verde = OK, rojo = sin datos).',
        'Datos decodificados: Los valores extraídos del mensaje GOES.',
    ])

    pdf.section_title('Indicadores de salud')
    pdf.body_text(
        'Los indicadores de color te ayudan a identificar rápidamente '
        'estaciones con problemas de transmisión. Una estación en rojo '
        'puede requerir mantenimiento preventivo o revisión del equipo transmisor.'
    )
    pdf.tip_box('Si una estación lleva más de 4 horas sin transmitir, notifícalo al equipo de mantenimiento.')

    # ═══════════════════════════════════════════════════════════
    # CAPÍTULO 8: REPOSITORIO DE DOCUMENTOS
    # ═══════════════════════════════════════════════════════════
    pdf.chapter_title('Repositorio de Documentos')

    pdf.section_title('Ver documentos disponibles')
    pdf.body_text(
        'El Repositorio almacena documentos operativos organizados por producto '
        '(ej: Boletín Hidrológico, Reporte de Funcionamiento de Vasos, etc.).'
    )
    pdf.steps([
        'En el menú, haz clic en "Repositorio" (ícono de carpeta).',
        'Verás tarjetas de cada producto disponible según tus permisos.',
        'Cada tarjeta muestra: nombre del producto, si es requerido diariamente, y el último documento subido.',
        'Haz clic en una tarjeta para ver el historial de documentos de ese producto.',
    ])
    pdf.warning_box('Si ves un badge rojo en el menú "Repositorio", significa que hay documentos pendientes de subir hoy.')

    pdf.section_title('Subir un documento')
    pdf.body_text(
        'Solo los roles Operador y Administrador pueden subir documentos.'
    )
    pdf.steps([
        'Entra al producto donde deseas subir el documento.',
        'Haz clic en el botón "Subir documento" o en el ícono de carga.',
        'Selecciona el archivo desde tu computadora (PDF, Excel, Word, etc.).',
        'Opcionalmente agrega una nota o descripción.',
        'Haz clic en "Subir". El documento quedará registrado con fecha, hora y tu nombre de usuario.',
    ])

    pdf.section_title('Historial y calendario')
    pdf.body_text(
        'El historial muestra un calendario mensual donde puedes ver qué días '
        'se subió el documento y cuáles están pendientes. Los días con documento '
        'aparecen marcados en verde, los pendientes en rojo.'
    )

    pdf.section_title('Bitácora de auditoría')
    pdf.body_text(
        'Los administradores pueden consultar la bitácora de auditoría que registra '
        'quién subió, descargó o eliminó cada documento, con fecha y hora exacta.'
    )

    # ═══════════════════════════════════════════════════════════
    # CAPÍTULO 9: FUNCIONAMIENTO DE VASOS
    # ═══════════════════════════════════════════════════════════
    pdf.chapter_title('Funcionamiento de Vasos')

    pdf.section_title('Tabla de datos por presa')
    pdf.body_text(
        'El módulo de Funcionamiento de Vasos muestra los datos operativos de las '
        '4 presas del sistema Grijalva más el Tapón Juan Grijalva.'
    )
    pdf.body_text('Para cada presa verás una tabla detallada con:')
    pdf.bullet_list([
        'Hora del registro.',
        'Elevación (metros sobre el nivel del mar).',
        'Almacenamiento (millones de metros cúbicos).',
        'Diferencia de almacenamiento respecto al registro anterior.',
        'Aportaciones (caudal de entrada al vaso).',
        'Turbinas o canal + túneles (caudal de salida).',
        'Vertedor (caudal vertido).',
        'Total de extracciones.',
        'Generación (MWH) y unidades generadoras activas.',
        'Cuenca propia y promedio.',
    ])

    pdf.section_title('Gráfica elevación-almacenamiento')
    pdf.body_text(
        'Debajo de cada tabla de presa se muestra la curva de '
        'elevaciones-almacenamientos (curva de capacidades del vaso). '
        'Los datos actuales se superponen a la curva de referencia para '
        'visualizar qué tan lleno está el vaso.'
    )

    pdf.section_title('Líneas de referencia')
    pdf.body_text(
        'Los administradores pueden agregar líneas de referencia a las gráficas '
        '(como NAMO, NAME, NAMINO). Estas líneas horizontales ayudan a visualizar '
        'de un vistazo si la presa está en niveles normales, altos o bajos.'
    )
    pdf.tip_box('Si ves que la elevación actual está cerca de una línea roja (NAME), es señal de alerta. Comunícalo inmediatamente al área de operación.')

    # ═══════════════════════════════════════════════════════════
    # CAPÍTULO 10: PRONÓSTICO DE LLUVIA
    # ═══════════════════════════════════════════════════════════
    pdf.chapter_title('Pronóstico de Lluvia')

    pdf.section_title('Resumen por cuenca')
    pdf.body_text(
        'El módulo de Pronóstico de Lluvia muestra las proyecciones de precipitación '
        'para las cuencas del sistema Grijalva, generadas a partir de modelos numéricos '
        'del Servicio Meteorológico Nacional (SMN).'
    )
    pdf.body_text(
        'En la parte superior verás 4 tarjetas con el resumen por cuenca:'
    )
    pdf.bullet_list([
        'Angostura: Cuenca de la presa La Angostura.',
        'Chicoasén: Cuenca de la presa chicoasen (Manuel Moreno Torres).',
        'Malpaso: Cuenca de la presa Netzahualcóyotl (Malpaso).',
        'Peñitas: Cuenca de la presa Ángel Albino Corzo (Peñitas).',
    ])
    pdf.body_text(
        'Cada tarjeta muestra la precipitación acumulada pronosticada a 24h, 48h y 72h, '
        'además de la precipitación máxima esperada.'
    )

    pdf.section_title('Gráficas de precipitación')
    pdf.body_text(
        'Las gráficas muestran la distribución horaria de la precipitación pronosticada:'
    )
    pdf.bullet_list([
        'Barras: Precipitación horaria pronosticada (mm/h).',
        'Línea acumulada: Total acumulado a lo largo del horizonte de pronóstico.',
        'Puedes seleccionar diferentes cuencas haciendo clic en las tarjetas superiores.',
    ])

    pdf.section_title('Tabla de pronóstico detallado')
    pdf.body_text(
        'Debajo de las gráficas encontrarás una tabla con los valores numéricos '
        'de cada hora del pronóstico, útil para reportes y planificación operativa.'
    )

    # ═══════════════════════════════════════════════════════════
    # CAPÍTULO 11: CHAT EN TIEMPO REAL
    # ═══════════════════════════════════════════════════════════
    pdf.chapter_title('Chat en Tiempo Real')

    pdf.section_title('Salas de conversación')
    pdf.body_text(
        'El Chat te permite comunicarte en tiempo real con otros usuarios de la PIH. '
        'Está organizado en salas temáticas:'
    )
    pdf.bullet_list([
        'General: Conversación abierta para todo el equipo.',
        'Operación: Coordinación de operaciones diarias.',
        'Alertas: Discusión de eventos hidrometeorológicos importantes.',
        'Hidrología: Temas técnicos de hidrología e ingeniería.',
    ])

    pdf.section_title('Enviar y recibir mensajes')
    pdf.steps([
        'En el menú, haz clic en "Chat" (ícono de conversación).',
        'Selecciona la sala en el panel izquierdo.',
        'Escribe tu mensaje en el campo de texto de la parte inferior.',
        'Presiona Enter o haz clic en "Enviar".',
        'Tu mensaje aparecerá en la conversación y todos los demás usuarios lo verán al instante.',
    ])
    pdf.body_text(
        'Los mensajes se guardan en el servidor, así que al reconectarte podrás ver '
        'el historial de conversaciones anteriores.'
    )

    pdf.section_title('Usuarios en línea')
    pdf.body_text(
        'En el panel derecho verás la lista de usuarios conectados actualmente. '
        'Un punto verde junto al nombre indica que el usuario está en línea. '
        'Cuando alguien se conecta o desconecta, verás una notificación en la sala.'
    )
    pdf.tip_box('El chat funciona con tecnología SignalR, que mantiene una conexión persistente. Si pierdes conexión, el sistema intentará reconectarse automáticamente.')

    # ═══════════════════════════════════════════════════════════
    # CAPÍTULO 12: ADMINISTRACIÓN
    # ═══════════════════════════════════════════════════════════
    pdf.chapter_title('Administración (solo Administradores)')
    pdf.body_text(
        'Los módulos de administración solo están disponibles para usuarios con '
        'el rol Administrador. Se acceden desde el menú desplegable "Administración" '
        '(ícono de engranajes) en la barra superior.'
    )
    pdf.warning_box('Los cambios realizados en estos módulos afectan a todo el sistema y a todos los usuarios. Procede con cuidado.')

    # 12.1 Estaciones
    pdf.section_title('Gestión de Estaciones')
    pdf.body_text(
        'El catálogo de estaciones permite ver y editar toda la información de las '
        'estaciones hidrometeorológicas del sistema.'
    )
    pdf.body_text('En la pantalla principal verás la tabla de estaciones con:')
    pdf.bullet_list([
        'Nombre de la estación.',
        'ID Asignado (código interno).',
        'Tipo de telemetría: GOES, GPRS, RADIO.',
        'Estado: Activa o Inactiva.',
        'Botón de Editar para configurar cada estación.',
    ])
    pdf.body_text('Al editar una estación encontrarás pestañas con:')
    pdf.bullet_list([
        'Datos Generales: Nombre, ubicación, coordenadas, cuenca y subcuenca.',
        'Telemetría: Configuración de los canales GOES, GPRS y Radio.',
        'Sensores: Lista de sensores instalados (pluviómetro, limnímetro, etc.).',
        'Umbrales: Valores de alerta para cada sensor (niveles de precaución, alarma, etc.).',
        'Mapa: Ubicación geográfica de la estación.',
    ])
    pdf.tip_box('Recuerda que los umbrales configurados aquí son los que usa el sistema de Alertas Tempranas para enviar notificaciones automáticas.')

    # 12.2 Mantenimiento
    pdf.section_title('Órdenes de Mantenimiento')
    pdf.body_text(
        'El módulo de Mantenimiento permite gestionar las órdenes de trabajo '
        'para el mantenimiento de las estaciones.'
    )
    pdf.body_text('Estados de una orden:')
    pdf.bullet_list([
        'Programado: La orden está creada pero aún no se inicia.',
        'En Proceso: El técnico está trabajando en la estación.',
        'Completado: El mantenimiento se terminó exitosamente.',
        'Cancelado: La orden fue cancelada.',
    ])
    pdf.body_text('Tipos de mantenimiento:')
    pdf.bullet_list([
        'Preventivo: Mantenimiento programado regular.',
        'Correctivo: Reparación por falla.',
        'Instalación: Instalación de nuevo equipo.',
        'Retiro: Desinstalación de equipo.',
        'Calibración: Ajuste y calibración de sensores.',
        'Emergencia: Atención urgente por evento extraordinario.',
    ])
    pdf.body_text(
        'Cada orden tiene una bitácora donde puedes registrar avances, '
        'adjuntar fotografías y documentos de soporte.'
    )

    # 12.3 Cuencas
    pdf.section_title('Cuencas y Subcuencas')
    pdf.body_text(
        'Este módulo permite administrar la jerarquía de cuencas y subcuencas:'
    )
    pdf.steps([
        'Crear o editar cuencas y subcuencas con su nombre, código y color.',
        'Asignar archivos KML para la visualización en el mapa.',
        'Asignar estaciones a cada cuenca y subcuenca.',
        'Activar/desactivar la visibilidad de capas en el mapa.',
    ])

    # 12.4 Usuarios
    pdf.section_title('Gestión de Usuarios')
    pdf.body_text(
        'El administrador puede gestionar todos los usuarios del sistema:'
    )
    pdf.steps([
        'Ver la lista completa de usuarios con su rol y estado.',
        'Crear nuevos usuarios con rol asignado (Visualizador, Operador, Administrador).',
        'Activar o desactivar cuentas sin eliminarlas.',
        'Cambiar el rol de un usuario.',
        'Asignar permisos de documentos: definir qué productos puede ver cada usuario.',
        'Restablecer la contraseña de un usuario.',
    ])
    pdf.warning_box('Al desactivar un usuario, este no podrá iniciar sesión pero sus datos históricos se conservan intactos.')

    # 12.5 Notificaciones
    pdf.section_title('Centro de Notificaciones')
    pdf.body_text(
        'El Centro de Notificaciones permite enviar correos electrónicos '
        'desde la plataforma. Tiene tres funciones principales:'
    )
    pdf.body_text('Pestaña "Correo Personalizado":')
    pdf.steps([
        'Selecciona los destinatarios del dropdown (puedes elegir varios).',
        'Usa el botón "Seleccionar Todos" para enviar a todos los usuarios.',
        'Escribe el asunto del correo.',
        'Escribe el cuerpo del mensaje (acepta formato HTML).',
        'Haz clic en "Enviar Correo".',
    ])
    pdf.body_text('Pestaña "Alerta Manual":')
    pdf.body_text(
        'Permite enviar una alerta predefinida indicando estación, variable, '
        'valor actual y umbral superado. El sistema genera automáticamente '
        'un correo de alerta con formato profesional.'
    )
    pdf.body_text('Pestaña "Prueba SMTP":')
    pdf.body_text(
        'Muestra la configuración actual del servidor SMTP y permite enviar '
        'un correo de prueba para verificar que el sistema de envío funciona correctamente.'
    )

    # 12.6 Alertas Tempranas
    pdf.section_title('Sistema de Alertas Tempranas')
    pdf.body_text(
        'El sistema de Alertas Tempranas monitorea automáticamente los valores '
        'de los sensores y envía notificaciones cuando se superan los umbrales '
        'configurados en las estaciones.'
    )
    pdf.body_text('La pantalla muestra:')
    pdf.bullet_list([
        'Tarjetas estadísticas: Umbrales activos, alertas en 24h, alertas en 7 días, alertas críticas.',
        'Pestaña Configuración: Activar/desactivar el sistema, ajustar el intervalo de evaluación (segundos) y el tiempo de enfriamiento (minutos) entre alertas repetidas.',
        'Pestaña Umbrales Monitoreados: Lista de todos los umbrales activos con su estación, sensor, variable, operador y valor de referencia.',
        'Pestaña Historial: Registro detallado de todas las alertas generadas, con fecha, estación, valor medido, umbral y estado de envío.',
    ])
    pdf.body_text('Cómo funciona:')
    pdf.steps([
        'El sistema lee automáticamente los datos más recientes de cada estación.',
        'Compara cada lectura contra los umbrales configurados (mayor que, menor que, etc.).',
        'Si un umbral se supera, genera una alerta y envía correo a los administradores.',
        'Se aplica un "enfriamiento" para no enviar la misma alerta repetidamente.',
        'Todo queda registrado en el historial para consulta posterior.',
    ])
    pdf.tip_box('Los umbrales se configuran en el módulo de Estaciones (pestaña Umbrales de cada sensor). Ajústalos cuidadosamente según las condiciones operativas de cada presa.')

    # ═══════════════════════════════════════════════════════════
    # GLOSARIO
    # ═══════════════════════════════════════════════════════════
    pdf.chapter_title('Glosario')
    pdf.key_value_table(
        ['Término', 'Definición'],
        [
            ['PIH', 'Plataforma Integral Hidrometeorológica'],
            ['GOES', 'Satélite Geoestacionario Operacional Ambiental (transmisión de datos)'],
            ['DCP', 'Plataforma de Colección de Datos (transmisor en estación)'],
            ['m.s.n.m.', 'Metros sobre el nivel del mar'],
            ['NAMO', 'Nivel de Aguas Máximo Ordinario'],
            ['NAME', 'Nivel de Aguas Máximo Extraordinario'],
            ['NAMINO', 'Nivel de Aguas Mínimo de Operación'],
            ['Cuenca', 'Área geográfica donde escurre el agua hacia un punto (presa)'],
            ['Subcuenca', 'División menor dentro de una cuenca'],
            ['Umbral', 'Valor límite que dispara una alerta al ser superado'],
            ['SignalR', 'Tecnología de comunicación en tiempo real (usada en el chat)'],
            ['KML', 'Formato de archivo para polígonos geográficos en mapas'],
            ['JWT', 'Token de autenticación para acceso desde aplicaciones móviles'],
            ['SMTP', 'Protocolo de envío de correo electrónico'],
            ['SMN', 'Servicio Meteorológico Nacional'],
            ['Aportación', 'Caudal de agua que entra al vaso de una presa'],
            ['Vertedor', 'Estructura para desalojar agua cuando la presa está muy llena'],
            ['Hietograma', 'Gráfica de precipitación contra tiempo'],
        ]
    )

    # ═══════════════════════════════════════════════════════════
    # PREGUNTAS FRECUENTES
    # ═══════════════════════════════════════════════════════════
    pdf.chapter_title('Preguntas Frecuentes')

    faqs = [
        ('¿Por qué no puedo ver el módulo de Administración?',
         'Solo los usuarios con rol Administrador pueden ver el menú de Administración. '
         'Si necesitas acceso, solicita a tu administrador que cambie tu rol.'),
        ('¿Qué hago si olvidé mi contraseña?',
         'En la pantalla de inicio de sesión, haz clic en "¿Olvidaste tu contraseña?" '
         'y sigue las instrucciones. Recibirás un correo con un enlace para restablecerla.'),
        ('¿Por qué una estación aparece sin datos?',
         'Puede deberse a: 1) La estación está en mantenimiento, 2) El transmisor GOES '
         'falló, 3) Hay un problema de comunicación. Consulta el módulo GOES para verificar.'),
        ('¿Cada cuánto se actualizan los datos?',
         'Los datos de las estaciones GOES se actualizan cada 1-4 horas según la '
         'programación del transmisor. Los datos GPRS pueden ser cada 15-30 minutos.'),
        ('¿Puedo usar la PIH desde mi celular?',
         'Sí, la PIH es responsiva y se adapta a pantallas móviles. También se está '
         'desarrollando una aplicación nativa con notificaciones push.'),
        ('¿Cómo exporto datos a Excel?',
         'En los módulos de Cortes Horarios y Análisis de Datos encontrarás el botón '
         '"Exportar Excel" que descarga los datos en formato .xlsx.'),
        ('¿Qué significan los colores en el mapa?',
         'Los colores son una escala que va de azul (valores bajos) a rojo (valores altos). '
         'La escala cambia según la variable seleccionada.'),
        ('¿El chat guarda el historial?',
         'Sí, todos los mensajes se guardan en el servidor. Al reconectarte verás los '
         'últimos 50 mensajes de cada sala.'),
        ('¿Puedo recibir alertas por correo electrónico?',
         'Sí, el sistema de Alertas Tempranas envía correos automáticos cuando se superan '
         'los umbrales configurados. Solicita a tu administrador que configure los umbrales.'),
        ('¿Quién puede subir documentos?',
         'Solo los usuarios con rol Operador o Administrador. Los Visualizadores solo '
         'pueden descargar y consultar los documentos ya publicados.'),
    ]

    for pregunta, respuesta in faqs:
        if pdf.get_y() > 220:
            pdf.add_page()
        pdf.set_x(pdf.l_margin)
        pdf.set_font('Arial', 'B', 11)
        pdf.set_text_color(0, 80, 160)
        pdf.multi_cell(w=pdf.w - pdf.l_margin - pdf.r_margin, h=6, text=f'P: {pregunta}')
        pdf.set_x(pdf.l_margin)
        pdf.set_font('Arial', '', 10.5)
        pdf.set_text_color(50, 50, 50)
        pdf.multi_cell(w=pdf.w - pdf.l_margin - pdf.r_margin, h=6, text=f'R: {respuesta}')
        pdf.ln(4)

    # ═══════════════════════════════════════════════════════════
    # CONTACTO Y SOPORTE
    # ═══════════════════════════════════════════════════════════
    pdf.chapter_title('Contacto y Soporte')
    pdf.body_text(
        'Si tienes problemas técnicos o dudas sobre el uso de la PIH, contacta '
        'al equipo de soporte:'
    )
    pdf.bullet_list([
        'Correo electrónico: cuenca.grijalva@cfe.gob.mx',
        'Área responsable: Subgerencia Regional de Generación Hidroeléctrica Grijalva',
        'Sistema: Plataforma Integral Hidrometeorológica (PIH) / CloudStation',
    ])
    pdf.body_text(
        'Al reportar un problema, incluye:'
    )
    pdf.bullet_list([
        'Tu nombre de usuario.',
        'Descripción del problema.',
        'Captura de pantalla (si es posible).',
        'Navegador y sistema operativo que usas.',
        'Fecha y hora en que ocurrió el problema.',
    ])

    # ── Guardar ────────────────────────────────────────────────
    output_path = os.path.join(os.path.dirname(__file__), 'Manual_Usuario_PIH.pdf')
    pdf.output(output_path)
    print(f'Manual generado exitosamente: {output_path}')
    print(f'Total de páginas: {pdf.pages_count}')
    return output_path


if __name__ == '__main__':
    build_manual()
