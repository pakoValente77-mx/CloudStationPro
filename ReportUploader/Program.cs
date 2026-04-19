using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

// === Configuración ===
string serverUrl = "http://localhost:5215";
string apiKey = "***REDACTED-API-KEY***";
string watchDir = Path.Combine(AppContext.BaseDirectory, "reportes");
int intervalSeconds = 60;

// Parse arguments
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--server" when i + 1 < args.Length: serverUrl = args[++i]; break;
        case "--key" when i + 1 < args.Length: apiKey = args[++i]; break;
        case "--watch" when i + 1 < args.Length: watchDir = args[++i]; break;
        case "--interval" when i + 1 < args.Length: intervalSeconds = int.Parse(args[++i]); break;
        case "--help":
            Console.WriteLine("ReportUploader — Auto-upload de reportes al catálogo PIH");
            Console.WriteLine();
            Console.WriteLine("Uso: ReportUploader [opciones]");
            Console.WriteLine("  --server URL     Servidor PIH (default: http://localhost:5215)");
            Console.WriteLine("  --key APIKEY     API key (default: ***REDACTED-API-KEY***)");
            Console.WriteLine("  --watch DIR      Directorio de reportes (default: ./reportes)");
            Console.WriteLine("  --interval SEC   Intervalo en segundos (default: 60)");
            Console.WriteLine();
            Console.WriteLine("Estructura esperada del directorio de reportes:");
            Console.WriteLine("  reportes/");
            Console.WriteLine("    1.png          → se mapea al comando /1");
            Console.WriteLine("    2.png          → se mapea al comando /2");
            Console.WriteLine("    reporte_lluvia_1_1_*.png → prefijo /6");
            Console.WriteLine("    reporte_lluvia_1_2_*.png → prefijo /7");
            Console.WriteLine("    <blobname>.png → nombre exacto del catálogo");
            return;
    }
}

Directory.CreateDirectory(watchDir);

Console.WriteLine("╔══════════════════════════════════════════════════╗");
Console.WriteLine("║     ReportUploader — PIH Auto-Upload Service    ║");
Console.WriteLine("╠══════════════════════════════════════════════════╣");
Console.WriteLine($"║  Servidor : {serverUrl,-37}║");
Console.WriteLine($"║  Watch    : {watchDir,-37}║");
Console.WriteLine($"║  Intervalo: {intervalSeconds,3}s{new string(' ', 33)}║");
Console.WriteLine("╚══════════════════════════════════════════════════╝");
Console.WriteLine();

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

// Track last upload time per file to avoid re-uploading unchanged files
var lastUploadTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            await UploadCycleAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR en ciclo: {ex.Message}");
        }

        await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cts.Token);
    }
}
catch (OperationCanceledException)
{
    // Normal shutdown
}

Console.WriteLine("\nReportUploader detenido.");

// === Funciones ===

async Task UploadCycleAsync()
{
    // 1) Obtener catálogo del servidor
    var catalog = await FetchCatalogAsync();
    if (catalog == null || catalog.Count == 0)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] No se pudo obtener el catálogo o está vacío.");
        return;
    }

    // 2) Buscar archivos en el directorio watch
    if (!Directory.Exists(watchDir))
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Directorio '{watchDir}' no existe.");
        return;
    }

    var files = Directory.GetFiles(watchDir, "*.*", SearchOption.TopDirectoryOnly)
        .Where(f => IsImageFile(f))
        .ToList();

    if (files.Count == 0)
        return;

    int uploaded = 0;
    int skipped = 0;

    foreach (var report in catalog)
    {
        var matchedFile = FindMatchingFile(files, report);
        if (matchedFile == null)
            continue;

        var fileInfo = new FileInfo(matchedFile);
        var fileKey = matchedFile.ToLowerInvariant();

        // Skip if file hasn't changed since last upload
        if (lastUploadTimes.TryGetValue(fileKey, out var lastTime) && fileInfo.LastWriteTimeUtc <= lastTime)
        {
            skipped++;
            continue;
        }

        // Upload
        var success = await UploadFileAsync(matchedFile, report);
        if (success)
        {
            lastUploadTimes[fileKey] = fileInfo.LastWriteTimeUtc;
            uploaded++;
        }
    }

    if (uploaded > 0 || skipped > 0)
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Ciclo: {uploaded} subidos, {skipped} sin cambios, {catalog.Count} reportes en catálogo");
}

async Task<List<ReportInfo>?> FetchCatalogAsync()
{
    try
    {
        var response = await http.GetAsync($"{serverUrl.TrimEnd('/')}/api/reports");
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error obteniendo catálogo: {response.StatusCode}");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<ReportInfo>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error conectando al servidor: {ex.Message}");
        return null;
    }
}

string? FindMatchingFile(List<string> files, ReportInfo report)
{
    // 1) Match by command number: e.g. "1.png", "2.jpg" for /1, /2
    var cmdNum = report.Command.TrimStart('/');
    var byNumber = files.FirstOrDefault(f =>
    {
        var name = Path.GetFileNameWithoutExtension(f);
        return name.Equals(cmdNum, StringComparison.OrdinalIgnoreCase);
    });
    if (byNumber != null) return byNumber;

    // 2) Match by LatestPrefix (e.g. "reporte_lluvia_1_1_*.png")
    if (!string.IsNullOrEmpty(report.LatestPrefix))
    {
        var byPrefix = files
            .Where(f => Path.GetFileName(f).StartsWith(report.LatestPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
            .FirstOrDefault();
        if (byPrefix != null) return byPrefix;
    }

    // 3) Match by exact BlobName
    if (!string.IsNullOrEmpty(report.BlobName))
    {
        var byBlob = files.FirstOrDefault(f =>
            Path.GetFileName(f).Equals(report.BlobName, StringComparison.OrdinalIgnoreCase));
        if (byBlob != null) return byBlob;
    }

    return null;
}

async Task<bool> UploadFileAsync(string filePath, ReportInfo report)
{
    try
    {
        var fileName = Path.GetFileName(filePath);
        var uploadName = report.BlobName ?? fileName;

        // If the report uses prefix-based lookup, keep the original filename
        if (!string.IsNullOrEmpty(report.LatestPrefix))
            uploadName = fileName;

        using var form = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(filePath));
        form.Add(fileContent, "file", fileName);

        var category = report.Category ?? "unidades";
        var url = $"{serverUrl.TrimEnd('/')}/api/images/{category}?name={Uri.EscapeDataString(uploadName)}";
        var response = await http.PostAsync(url, form);

        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine($"  ✓ {report.Command} ({report.Title}) → {uploadName}");
            return true;
        }

        var body = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"  ✗ {report.Command}: {response.StatusCode} - {body}");
        return false;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ✗ {report.Command}: {ex.Message}");
        return false;
    }
}

bool IsImageFile(string path)
{
    var ext = Path.GetExtension(path).ToLowerInvariant();
    return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" or ".pdf";
}

string GetMimeType(string path)
{
    var ext = Path.GetExtension(path).ToLowerInvariant();
    return ext switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".svg" => "image/svg+xml",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };
}

class ReportInfo
{
    public int Id { get; set; }
    public string Command { get; set; } = "";
    public string? ContentType { get; set; }
    public string? Title { get; set; }
    public string? Category { get; set; }
    public string? BlobName { get; set; }
    public string? LatestPrefix { get; set; }
}
