using System.Net.Http.Headers;
using System.Text.Json;

// === FunVasosUploader ===
// Sube archivos FIN Excel al API de CloudStation y los procesa en la BD.
// Uso:
//   FunVasosUploader <archivo_o_carpeta> [url_servidor]
//   FunVasosUploader FIN100426.xlsx
//   FunVasosUploader FIN100426.xlsx http://atlas16.ddns.net:5215
//   FunVasosUploader C:\FunVasos\inbox
//   FunVasosUploader --validate FIN100426.xlsx

string serverUrl = "http://localhost:5215";
string apiKey = "pih-grijalva-2026";
bool validateOnly = false;
string? targetPath = null;

// Parsear argumentos
var argList = args.ToList();
if (argList.Remove("--validate") || argList.Remove("-v"))
    validateOnly = true;

if (argList.Count < 1)
{
    Console.WriteLine("FunVasosUploader — Sube archivos FIN Excel a CloudStation");
    Console.WriteLine();
    Console.WriteLine("Uso:");
    Console.WriteLine("  FunVasosUploader <archivo.xlsx>           Sube un archivo");
    Console.WriteLine("  FunVasosUploader <carpeta>                Sube todos los .xlsx de la carpeta");
    Console.WriteLine("  FunVasosUploader --validate <archivo>     Solo valida sin insertar");
    Console.WriteLine("  FunVasosUploader <archivo> <url_servidor> Especifica servidor");
    Console.WriteLine();
    Console.WriteLine($"Servidor por defecto: {serverUrl}");
    return;
}

targetPath = argList[0];
if (argList.Count > 1)
    serverUrl = argList[1];

// Determinar archivos a procesar
var files = new List<string>();
if (Directory.Exists(targetPath))
{
    files.AddRange(Directory.GetFiles(targetPath, "*.xlsx"));
    files.AddRange(Directory.GetFiles(targetPath, "*.xls"));
    files.AddRange(Directory.GetFiles(targetPath, "*.xlsm"));
    files.Sort();
    if (files.Count == 0)
    {
        Console.WriteLine($"No se encontraron archivos Excel en '{targetPath}'");
        return;
    }
    Console.WriteLine($"Encontrados {files.Count} archivo(s) en '{targetPath}'");
}
else if (File.Exists(targetPath))
{
    files.Add(targetPath);
}
else
{
    Console.WriteLine($"Error: No se encontró '{targetPath}'");
    return;
}

string endpoint = validateOnly ? "validate" : "upload";
Console.WriteLine($"Servidor: {serverUrl}");
Console.WriteLine($"Modo: {(validateOnly ? "VALIDACIÓN" : "CARGA")}");
Console.WriteLine(new string('─', 50));

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

int ok = 0, fail = 0;

foreach (var filePath in files)
{
    var fileName = Path.GetFileName(filePath);
    Console.Write($"  {fileName} ... ");

    try
    {
        using var form = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(fileContent, "file", fileName);

        var url = $"{serverUrl.TrimEnd('/')}/api/funvasos/{endpoint}";
        var response = await http.PostAsync(url, form);
        var body = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (validateOnly)
            {
                var date = root.GetProperty("dateFromExcel").GetString();
                var size = root.GetProperty("sizeBytes").GetInt64();
                Console.WriteLine($"OK (fecha: {date}, {size:N0} bytes)");
            }
            else
            {
                var date = root.GetProperty("date").GetString();
                var rows = root.GetProperty("rowsInserted").GetInt32();
                Console.WriteLine($"OK ({rows} registros, fecha: {date})");
            }
            ok++;
        }
        else
        {
            Console.WriteLine($"ERROR ({response.StatusCode}): {body}");
            fail++;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: {ex.Message}");
        fail++;
    }
}

Console.WriteLine(new string('─', 50));
Console.WriteLine($"Resultado: {ok} exitoso(s), {fail} error(es)");

if (fail > 0)
    Environment.ExitCode = 1;
