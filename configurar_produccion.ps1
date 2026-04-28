################################################################################
#  configurar_produccion.ps1
#  Script para configurar variables de entorno del servidor IIS en producción.
#  Reemplaza appsettings.json como fuente de secretos.
#
#  INSTRUCCIONES:
#  1. Edita los valores entre comillas vacías "" con las credenciales reales.
#  2. Ejecuta como Administrador en el servidor de producción:
#       powershell -ExecutionPolicy Bypass -File .\configurar_produccion.ps1
#  3. Reinicia el Application Pool de IIS al terminar.
#
#  Las variables usan __ (doble guión bajo) como separador de sección,
#  que ASP.NET Core lee automáticamente.
################################################################################

param(
    [switch]$Verify  # Usa -Verify para solo verificar sin modificar
)

$ErrorActionPreference = "Stop"

# ─── EDITA ESTOS VALORES ─────────────────────────────────────────────────────
$secrets = @{
    # Base de datos
    "ConnectionStrings__SqlServer"  = "Server=atlas16.ddns.net;Database=IGSCLOUD;User Id=pih_app;Password=NUEVA_CLAVE_BD;TrustServerCertificate=True;Connect Timeout=30;"
    "ConnectionStrings__PostgreSQL" = "Host=atlas16.ddns.net;Username=postgres;Password=NUEVA_CLAVE_PG;Database=mycloud_timescale"

    # JWT (generar con: openssl rand -hex 32)
    "Jwt__Key"                      = "NUEVA_CLAVE_JWT_64_CHARS_MINIMO"
    "Jwt__Issuer"                   = "CloudStationWeb"
    "Jwt__Audience"                 = "CloudStationAPI"

    # API Key de la aplicación (generar con: openssl rand -hex 32)
    "ImageStore__ApiKey"            = "NUEVA_API_KEY_GENERADA_ALEATORIAMENTE"
    "ImageStore__Path"              = "C:\inetpub\wwwroot\pih\ImageStore"

    # SMTP
    "Email__Smtp__Host"             = "159.16.6.20"
    "Email__Smtp__Port"             = "587"
    "Email__Smtp__Username"         = "cuenca.grijalva@cfe.gob.mx"
    "Email__Smtp__Password"         = "NUEVA_CLAVE_SMTP"
    "Email__Smtp__From"             = "cuenca.grijalva@cfe.gob.mx"
    "Email__Smtp__EnableSsl"        = "true"

    # AI APIs
    "Gemini__ApiKey"                = "NUEVA_GEMINI_API_KEY"
    "DeepSeek__ApiKey"              = "NUEVA_DEEPSEEK_API_KEY"

    # Google OAuth (opcional)
    "Authentication__Google__ClientId"     = ""
    "Authentication__Google__ClientSecret" = ""

    # Telegram Webhook
    "Webhook__Telegram__BotToken"   = "TU_BOT_TOKEN"
    "Webhook__Telegram__SecretToken" = "SECRETO_ALEATORIO_PARA_VALIDAR_WEBHOOK"

    # CORS - dominios autorizados en producción
    "Cors__AllowedOrigins__0"       = "https://pih.cfe.gob.mx"

    # Sesión
    "Security__Session__ExpireHours"         = "8"
    "Security__Session__SlidingExpiration"   = "false"
}
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "`n=== Configuración de Variables de Entorno — PIH Producción ===" -ForegroundColor Cyan
Write-Host "Modo: $(if ($Verify) { 'VERIFICACIÓN (sin cambios)' } else { 'ESCRITURA' })`n" -ForegroundColor Yellow

$ok = 0
$pending = 0
$total = $secrets.Count

foreach ($kv in $secrets.GetEnumerator()) {
    $current = [System.Environment]::GetEnvironmentVariable($kv.Key, "Machine")

    if ($Verify) {
        if ($null -ne $current) {
            Write-Host "  [OK] $($kv.Key)" -ForegroundColor Green
            $ok++
        } else {
            Write-Host "  [--] $($kv.Key) (no configurada)" -ForegroundColor Red
            $pending++
        }
    } else {
        if ([string]::IsNullOrWhiteSpace($kv.Value) -or $kv.Value -match "^(NUEVA|TU_|SECRETO_)") {
            Write-Host "  [SKIP] $($kv.Key) — valor no configurado, omitido" -ForegroundColor DarkYellow
            $pending++
            continue
        }
        [System.Environment]::SetEnvironmentVariable($kv.Key, $kv.Value, "Machine")
        Write-Host "  [SET]  $($kv.Key)" -ForegroundColor Green
        $ok++
    }
}

Write-Host "`n─────────────────────────────────────────────────────────────" -ForegroundColor DarkGray
if ($Verify) {
    Write-Host "  Configuradas: $ok / $total   Pendientes: $pending" -ForegroundColor $(if ($pending -gt 0) { "Yellow" } else { "Green" })
} else {
    Write-Host "  Escritas: $ok    Omitidas: $pending" -ForegroundColor $(if ($pending -gt 0) { "Yellow" } else { "Green" })

    if ($ok -gt 0) {
        Write-Host "`n  IMPORTANTE: Reinicia el Application Pool de IIS para aplicar los cambios:" -ForegroundColor Cyan
        Write-Host "    iisreset /noforce" -ForegroundColor White
        Write-Host "  o desde el Administrador de IIS → Application Pools → Reciclar" -ForegroundColor White
    }
}
Write-Host ""
