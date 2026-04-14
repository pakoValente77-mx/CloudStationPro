#!/bin/bash
# =====================================================================
# CLOUDSTATION PIH - Script de Publicación Producción
# Fecha: 2026-06-10
# Dominio: hidrometria.mx (Cloudflare SSL)
# Hosting: IIS Windows (in-process, AspNetCoreModuleV2)
# Cambios v3.1:
#   - Menú navegación rediseñado: 13 items → 6 dropdowns agrupados
#     (Monitoreo, Análisis, Hidrología, Admin, Herramientas)
#   - Filtro "Solo CFE" en todas las pantallas de estaciones
#     (GoesMonitoring, Maintenance, MisExport)
#   - Mapa responsive: paneles colapsables con toggle buttons
#     (breakpoints 1200px, 1024px, 768px para iPad/tablet)
#   - Tema claro coherente en mapa (glass-panel, tablas, dropdowns,
#     controles zoom, banner clima, inputs, checkboxes)
#   - Dropdown dark theme fix (selectores alta especificidad Fomantic)
# Cambios v3.0:
#   - API REST Pronóstico Hidrológico (formato Spring Boot)
#   - Autenticación dual: API Key + JWT (rol ApiConsumer)
#   - FunVasos API + FunVasosUploader
#   - ImageStore local (reemplaza Azure Blob)
#   - Gráficas de tendencia en Funcionamiento de Vasos
#   - UmbralAlertas (alertas tempranas por sensor)
#   - ForwardedHeaders para Cloudflare proxy
#   - Centinela AI: Gráficas server-side, enrutamiento inteligente
#   - Chat: archivos adjuntos, app escritorio, auth dual SignalR
# =====================================================================

set -e

PROJ_DIR="$(cd "$(dirname "$0")" && pwd)"
PUBLISH_DIR="$PROJ_DIR/publish_produccion"
PUBLICADO_OK="$PROJ_DIR/../publicado_ok"

echo "=============================================="
echo " CLOUDSTATION PIH - BUILD PRODUCCION"
echo " $(date '+%Y-%m-%d %H:%M:%S')"
echo "=============================================="
echo ""

# 1. Limpiar
echo "[1/5] Limpiando build anterior..."
rm -rf "$PUBLISH_DIR" 2>/dev/null
dotnet clean "$PROJ_DIR/CloudStationWeb.csproj" -c Release --nologo -q

# 2. Compilar en Release
echo "[2/5] Compilando en modo Release..."
dotnet build "$PROJ_DIR/CloudStationWeb.csproj" -c Release --nologo -q
echo "   Build OK (0 errores)"

# 3. Publicar self-contained para Windows x64
echo "[3/5] Publicando para win-x64 (self-contained)..."
dotnet publish "$PROJ_DIR/CloudStationWeb.csproj" \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -o "$PUBLISH_DIR" \
    --nologo -q

echo "   Publicado en: $PUBLISH_DIR"

# 4. Copiar archivos adicionales
echo "[4/5] Copiando archivos adicionales..."

# appsettings de producción
cp "$PROJ_DIR/appsettings.json" "$PUBLISH_DIR/"
cp "$PROJ_DIR/appsettings.Development.json" "$PUBLISH_DIR/" 2>/dev/null || true

# web.config para IIS
if [ -f "$PROJ_DIR/web.config" ]; then
    cp "$PROJ_DIR/web.config" "$PUBLISH_DIR/"
fi

# package.json (si lo requiere)
if [ -f "$PROJ_DIR/package.json" ]; then
    cp "$PROJ_DIR/package.json" "$PUBLISH_DIR/"
fi

# KML de cuencas
if [ -d "$PROJ_DIR/KML Cuencas y Rios Grijalva" ]; then
    cp -r "$PROJ_DIR/KML Cuencas y Rios Grijalva" "$PUBLISH_DIR/"
fi

# Scripts de deploy SQL para referencia
cp "$PROJ_DIR/deploy_produccion_v2.sql" "$PUBLISH_DIR/" 2>/dev/null || true
cp "$PROJ_DIR/deploy_timescale_v2.sql" "$PUBLISH_DIR/" 2>/dev/null || true

echo "   Archivos copiados"

# 5. Copiar a publicado_ok si existe
echo "[5/5] Actualizando carpeta publicado_ok..."
if [ -d "$PUBLICADO_OK" ]; then
    rsync -a --delete "$PUBLISH_DIR/" "$PUBLICADO_OK/"
    echo "   publicado_ok actualizado"
else
    echo "   AVISO: $PUBLICADO_OK no existe, solo se generó en publish_produccion/"
fi

echo ""
echo "=============================================="
echo " BUILD COMPLETADO"
echo "=============================================="
echo ""
echo " Archivos generados en: $PUBLISH_DIR"
echo ""
echo " PASOS MANUALES EN SERVIDOR:"
echo " 1. Detener sitio IIS: CloudStation"
echo " 2. Copiar contenido de publish_produccion/ al servidor"
echo " 3. Ejecutar deploy_produccion_v2.sql en SQL Server (IGSCLOUD)"
echo " 4. Ejecutar deploy_timescale_v2.sql en PostgreSQL (mycloud_timescale)"
echo " 5. Crear carpetas junto al exe:"
echo "      mkdir ChatUploads"
echo "      mkdir ImageStore"
echo " 6. Configurar appsettings.json con IPs/passwords de produccion"
echo " 7. Configurar binding IIS: hidrometria.mx, puerto 5215"
echo " 8. Cloudflare: DNS A record → IP servidor, Proxy ON, SSL Full"
echo " 9. Iniciar sitio IIS"
echo ""
echo " COMANDOS PostgreSQL (si no hay psql, usar desde Python):"
echo "   psql -h atlas16.ddns.net -p 5432 -U postgres -d mycloud_timescale"
echo "   \\i deploy_postgres_v2.sql"
echo ""
