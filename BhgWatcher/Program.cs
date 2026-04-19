using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

// ══════════════════════════════════════════════════════════════
// BhgWatcher — Servicio Windows para monitoreo de archivos BHG
// Detecta archivos nuevos en OneDrive y los sube vía API a los
// endpoints configurados en appsettings.json.
//
// Instalación como servicio:
//   sc.exe create BhgWatcher binPath= "C:\...\BhgWatcher.exe"
//   sc.exe start BhgWatcher
//
// Ejecución como consola (para pruebas):
//   BhgWatcher.exe
// ══════════════════════════════════════════════════════════════

var builder = Host.CreateApplicationBuilder(args);

var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
builder.Configuration.AddJsonFile(Path.Combine(exeDir, "appsettings.json"), optional: false, reloadOnChange: true);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "BhgWatcher";
});

builder.Services.AddHostedService<BhgWatcherService>();

var host = builder.Build();
host.Run();

// ══════════════════════════════════════════════════════════════
// Modelo de endpoint
// ══════════════════════════════════════════════════════════════
public class BhgEndpoint
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

// ══════════════════════════════════════════════════════════════
// Servicio principal
// ══════════════════════════════════════════════════════════════
public class BhgWatcherService : BackgroundService
{
    private readonly ILogger<BhgWatcherService> _logger;
    private readonly IConfiguration _config;
    private readonly ConcurrentDictionary<string, string> _processedHashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _http;

    private string _oneDriveBase = "";
    private List<BhgEndpoint> _endpoints = new();
    private int _intervalMinutes = 10;
    private string[] _filePatterns = { "bhg*.*" };
    private HashSet<string> _extensions = new(StringComparer.OrdinalIgnoreCase) { ".xlsx", ".xlsm", ".xls" };
    private int _timeoutSeconds = 60;
    private int _maxRetries = 3;

    private FileSystemWatcher? _watcher;
    private FileSystemWatcher? _yearWatcher;
    private string? _watchedFolder;

    // Estado persistente: archivos ya subidos exitosamente
    private readonly string _stateFilePath;

    private static readonly string[] MesesEs = { "", "ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO",
        "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE" };

    public BhgWatcherService(ILogger<BhgWatcherService> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;

        var handler = new HttpClientHandler();
        // Permitir certificados auto-firmados en desarrollo
        handler.ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true;
        _http = new HttpClient(handler);

        var dataDir = Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory,
            "data");
        Directory.CreateDirectory(dataDir);
        _stateFilePath = Path.Combine(dataDir, "uploaded_hashes.json");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LoadConfig();
        LoadState();
        PrintBanner();

        if (!Directory.Exists(_oneDriveBase))
        {
            _logger.LogError("No se encontró la carpeta OneDrive: {Path}", _oneDriveBase);
            return;
        }

        if (_endpoints.Count == 0)
        {
            _logger.LogError("No hay endpoints configurados en appsettings.json");
            return;
        }

        // Escaneo inicial
        _logger.LogInformation("Escaneo inicial...");
        await ScanAndUploadAsync(stoppingToken);

        // Configurar FileSystemWatcher
        SetupWatchers();

        _logger.LogInformation("Esperando archivos... (polling cada {Interval} min)", _intervalMinutes);

        // Loop principal
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(_intervalMinutes), stoppingToken);

                ReloadEndpoints();

                var newMonthFolder = GetCurrentMonthFolder();
                if (newMonthFolder != null && newMonthFolder != _watchedFolder)
                {
                    _watcher?.Dispose();
                    _watcher = CreateWatcher(newMonthFolder);
                    _watchedFolder = newMonthFolder;
                    _logger.LogInformation("Carpeta de mes actualizada: {Folder}", newMonthFolder);
                }

                await ScanAndUploadAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ciclo principal");
            }
        }

        _watcher?.Dispose();
        _yearWatcher?.Dispose();
        _logger.LogInformation("BhgWatcher finalizado.");
    }

    // ── Configuración ──
    private void LoadConfig()
    {
        var section = _config.GetSection("BhgWatcher");

        _oneDriveBase = section["OneDriveBase"] ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            @"OneDrive - COMISION FEDERAL DE ELECTRICIDAD",
            @"Archivos de Hidrometria Grijalva - BOLETÍN HIDROMETEOROLÓGICO Y DE GENERACIÓN");

        _endpoints = section.GetSection("Endpoints").Get<List<BhgEndpoint>>() ?? new();
        _endpoints = _endpoints.Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.Url)).ToList();

        _intervalMinutes = section.GetValue("IntervalMinutes", 10);
        _timeoutSeconds = section.GetValue("TimeoutSeconds", 60);
        _maxRetries = section.GetValue("MaxRetries", 3);

        var patterns = section.GetSection("FilePatterns").Get<string[]>();
        if (patterns is { Length: > 0 }) _filePatterns = patterns;

        var exts = section.GetSection("Extensions").Get<string[]>();
        if (exts is { Length: > 0 }) _extensions = new HashSet<string>(exts, StringComparer.OrdinalIgnoreCase);

        _http.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);
    }

    private void ReloadEndpoints()
    {
        var section = _config.GetSection("BhgWatcher");
        var newEndpoints = section.GetSection("Endpoints").Get<List<BhgEndpoint>>() ?? new();
        newEndpoints = newEndpoints.Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.Url)).ToList();

        if (newEndpoints.Count != _endpoints.Count ||
            !newEndpoints.Select(e => e.Url).SequenceEqual(_endpoints.Select(e => e.Url)))
        {
            _endpoints = newEndpoints;
            _logger.LogInformation("Endpoints recargados: {Names}",
                string.Join(", ", _endpoints.Select(e => $"{e.Name} ({e.Url})")));
        }
    }

    private void PrintBanner()
    {
        _logger.LogInformation("╔══════════════════════════════════════════════════════════════╗");
        _logger.LogInformation("║              BHG Watcher — CloudStation Pro                 ║");
        _logger.LogInformation("╚══════════════════════════════════════════════════════════════╝");
        _logger.LogInformation("  OneDrive base: {Path}", _oneDriveBase);
        for (int i = 0; i < _endpoints.Count; i++)
            _logger.LogInformation("  Endpoint [{Num}/{Total}]: {Name} → {Url}",
                i + 1, _endpoints.Count, _endpoints[i].Name, _endpoints[i].Url);
        _logger.LogInformation("  Intervalo: {Min} min | Timeout: {Sec}s | Reintentos: {Ret}",
            _intervalMinutes, _timeoutSeconds, _maxRetries);
    }

    // ── Estado persistente ──
    private void LoadState()
    {
        if (!File.Exists(_stateFilePath)) return;
        try
        {
            var json = File.ReadAllText(_stateFilePath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict != null)
            {
                foreach (var kv in dict)
                    _processedHashes[kv.Key] = kv.Value;
            }
            _logger.LogInformation("{Count} archivo(s) en historial de subidas", _processedHashes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error cargando estado: {Error}", ex.Message);
        }
    }

    private void SaveState()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                _processedHashes.ToDictionary(kv => kv.Key, kv => kv.Value),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_stateFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error guardando estado: {Error}", ex.Message);
        }
    }

    // ── Carpetas OneDrive ──
    private string GetCurrentYearFolder()
    {
        int year = DateTime.Now.Year;
        var candidates = Directory.GetDirectories(_oneDriveBase, $"*{year}*");
        if (candidates.Length > 0) return candidates[0];
        var all = Directory.GetDirectories(_oneDriveBase)
            .Where(d => Path.GetFileName(d).StartsWith("BHG", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d).FirstOrDefault();
        return all ?? _oneDriveBase;
    }

    private string? GetCurrentMonthFolder()
    {
        var yearFolder = GetCurrentYearFolder();
        if (!Directory.Exists(yearFolder)) return null;

        int month = DateTime.Now.Month;
        string monthNum = month.ToString("D2");
        string monthName = MesesEs[month];

        var dirs = Directory.GetDirectories(yearFolder);
        foreach (var dir in dirs)
        {
            var name = Path.GetFileName(dir).ToUpper();
            if (name.StartsWith(monthNum) || name.Contains(monthName))
                return dir;
        }
        return dirs.OrderByDescending(d => d).FirstOrDefault();
    }

    // ── Hash SHA256 ──
    private static string ComputeFileHash(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static void WaitForFileReady(string filePath)
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                using var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return;
            }
            catch (IOException) { Thread.Sleep(1000); }
        }
    }

    // ── Subir archivo a un endpoint ──
    private async Task<bool> UploadToEndpointAsync(string filePath, BhgEndpoint endpoint, CancellationToken ct)
    {
        var fileName = Path.GetFileName(filePath);

        for (int attempt = 1; attempt <= _maxRetries; attempt++)
        {
            try
            {
                using var content = new MultipartFormDataContent();
                var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Add(fileContent, "file", fileName);

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url);
                request.Content = content;
                request.Headers.Add("X-Api-Key", endpoint.ApiKey);

                var response = await _http.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("✓ {File} → {Name} ({Status})",
                        fileName, endpoint.Name, response.StatusCode);
                    return true;
                }

                _logger.LogWarning("✗ {File} → {Name}: {Status} {Body} (intento {Attempt}/{Max})",
                    fileName, endpoint.Name, (int)response.StatusCode, body, attempt, _maxRetries);
            }
            catch (TaskCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning("✗ {File} → {Name}: {Error} (intento {Attempt}/{Max})",
                    fileName, endpoint.Name, ex.Message, attempt, _maxRetries);
            }

            if (attempt < _maxRetries)
                await Task.Delay(TimeSpan.FromSeconds(attempt * 5), ct);
        }

        return false;
    }

    // ── Subir a todos los endpoints ──
    private async Task<bool> UploadToAllAsync(string sourceFile, CancellationToken ct)
    {
        var fileName = Path.GetFileName(sourceFile);
        try
        {
            WaitForFileReady(sourceFile);

            var hash = ComputeFileHash(sourceFile);
            var upperName = fileName.ToUpper();

            if (_processedHashes.TryGetValue(upperName, out var existingHash) && existingHash == hash)
                return false;

            int okCount = 0;
            foreach (var endpoint in _endpoints)
            {
                if (await UploadToEndpointAsync(sourceFile, endpoint, ct))
                    okCount++;
            }

            if (okCount > 0)
            {
                _processedHashes[upperName] = hash;
                SaveState();
            }

            if (okCount < _endpoints.Count)
                _logger.LogWarning("{File}: subido a {Ok}/{Total} endpoints",
                    fileName, okCount, _endpoints.Count);

            return okCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error procesando {File}: {Error}", fileName, ex.Message);
            return false;
        }
    }

    // ── Escanear y subir ──
    private async Task<int> ScanAndUploadAsync(CancellationToken ct)
    {
        int uploaded = 0;
        var monthFolder = GetCurrentMonthFolder();
        if (monthFolder == null)
        {
            _logger.LogWarning("No se encontró carpeta de mes en OneDrive");
            return 0;
        }

        _logger.LogInformation("Escaneando: {Folder}", monthFolder);

        var allFiles = new List<string>();
        foreach (var pattern in _filePatterns)
        {
            try { allFiles.AddRange(Directory.GetFiles(monthFolder, pattern)); } catch { }
            try
            {
                foreach (var subDir in Directory.GetDirectories(monthFolder))
                    allFiles.AddRange(Directory.GetFiles(subDir, pattern));
            }
            catch { }
        }

        foreach (var file in allFiles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f))
        {
            if (!_extensions.Contains(Path.GetExtension(file).ToLower())) continue;
            if (Path.GetFileName(file).StartsWith("~$")) continue;
            if (await UploadToAllAsync(file, ct))
                uploaded++;
        }

        if (uploaded == 0)
            _logger.LogInformation("Sin archivos nuevos");
        else
            _logger.LogInformation("{Count} archivo(s) subido(s)", uploaded);

        return uploaded;
    }

    // ── Watchers ──
    private void SetupWatchers()
    {
        var monthFolder = GetCurrentMonthFolder();
        if (monthFolder != null)
        {
            _watcher = CreateWatcher(monthFolder);
            if (_watcher != null)
            {
                _watchedFolder = monthFolder;
                _logger.LogInformation("FileSystemWatcher activo en: {Folder}", monthFolder);
            }
        }

        var yearFolder = GetCurrentYearFolder();
        if (Directory.Exists(yearFolder))
        {
            _yearWatcher = new FileSystemWatcher(yearFolder)
            {
                NotifyFilter = NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };
            _yearWatcher.Created += (s, e) =>
            {
                _logger.LogInformation("Nueva carpeta detectada: {Name}", e.Name);
                var newMonth = GetCurrentMonthFolder();
                if (newMonth != null && newMonth != _watchedFolder)
                {
                    _watcher?.Dispose();
                    _watcher = CreateWatcher(newMonth);
                    _watchedFolder = newMonth;
                    _logger.LogInformation("FileSystemWatcher reconfigurado a: {Folder}", newMonth);
                }
            };
        }
    }

    private FileSystemWatcher? CreateWatcher(string folder)
    {
        if (!Directory.Exists(folder)) return null;

        var watcher = new FileSystemWatcher(folder)
        {
            Filter = "bhg*.*",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        var debounce = new ConcurrentDictionary<string, DateTime>();

        void HandleChange(object sender, FileSystemEventArgs e)
        {
            if (!_extensions.Contains(Path.GetExtension(e.FullPath).ToLower())) return;
            if (Path.GetFileName(e.FullPath).StartsWith("~$")) return;

            var now = DateTime.Now;
            if (debounce.TryGetValue(e.FullPath, out var last) && (now - last).TotalSeconds < 5)
                return;
            debounce[e.FullPath] = now;

            _logger.LogInformation("Cambio detectado: {File}", Path.GetFileName(e.FullPath));
            // Pausa para que OneDrive termine de sincronizar, luego subir
            Task.Run(async () =>
            {
                await Task.Delay(5000);
                await UploadToAllAsync(e.FullPath, CancellationToken.None);
            });
        }

        watcher.Created += HandleChange;
        watcher.Changed += HandleChange;
        watcher.Renamed += (s, e) => HandleChange(s, e);

        return watcher;
    }
}
