#!/bin/bash
# =====================================================================
# CLOUDSTATION PIH - Script de Publicación Producción
# Fecha: 2026-04-06
# Cambios incluidos:
#   - Centinela AI: Gráficas server-side con ScottPlot 5 (5 tipos)
#     * Elevación de presas, Generación MW, Precipitación barras
#     * Sensores por estación, Precipitación por cuenca
#   - Centinela AI: Enrutamiento inteligente de gráficas
#     * "Cuenca del Grijalva" / "cascada" → presas (5 puntos)
#     * "Precipitación por cuenca" → comparación cuencas
#   - Centinela AI: Búsqueda de estaciones en BD (NV_Estacion)
#   - Centinela AI: Filtro de variables (temp, nivel agua, etc.)
#   - Charts: Tema oscuro, upload Azure Blob, SAS URLs 1hr
#   - MIS Export: Header GOES reconstruido
#   - Chat: Soporte archivos adjuntos (drag & drop, paste, upload)
#   - Chat: App de escritorio (Electron) Windows + macOS
#   - Chat: Autenticación dual (Cookie + JWT) para SignalR
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
echo " 4. Ejecutar deploy_postgres_v2.sql en PostgreSQL (mycloud_timescale)"
echo " 5. Crear carpeta ChatUploads/ junto al exe (para archivos del chat)"
echo " 6. [Opcional] Copiar ChatDesktop/dist/ al servidor para descarga de app"
echo " 7. Iniciar sitio IIS"
echo ""
echo " COMANDOS PostgreSQL (si no hay psql, usar desde Python):"
echo "   psql -h atlas16.ddns.net -p 5432 -U postgres -d mycloud_timescale"
echo "   \\i deploy_postgres_v2.sql"
echo ""
