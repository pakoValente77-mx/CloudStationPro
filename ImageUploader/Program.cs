using System.Net.Http.Headers;

// === Configuración ===
string serverUrl = args.Length > 1 ? args[1] : "http://localhost:5215";
string apiKey = "***REDACTED-API-KEY***";
string category = "unidades";
string blobName = "9c8a7f42-3d91-4e01-a3fa-0d2e5b1c6f7d.png"; // Mismo nombre que /1

if (args.Length < 1)
{
    Console.WriteLine("Uso: ImageUploader <ruta_imagen> [url_servidor]");
    Console.WriteLine("Ejemplo: ImageUploader reporte.png http://atlas16.ddns.net:5215");
    return;
}

string imagePath = args[0];
if (!File.Exists(imagePath))
{
    Console.WriteLine($"Error: No se encontró el archivo '{imagePath}'");
    return;
}

Console.WriteLine($"Subiendo '{imagePath}' como {category}/{blobName}...");
Console.WriteLine($"Servidor: {serverUrl}");

using var http = new HttpClient();
http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

using var form = new MultipartFormDataContent();
var fileBytes = await File.ReadAllBytesAsync(imagePath);
var fileContent = new ByteArrayContent(fileBytes);
fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
form.Add(fileContent, "file", Path.GetFileName(imagePath));

var url = $"{serverUrl.TrimEnd('/')}/api/images/{category}?name={blobName}";
var response = await http.PostAsync(url, form);
var body = await response.Content.ReadAsStringAsync();

if (response.IsSuccessStatusCode)
{
    Console.WriteLine($"OK: {body}");
    Console.WriteLine($"\nAhora usa /1 en Centinela para ver la imagen.");
}
else
{
    Console.WriteLine($"Error ({response.StatusCode}): {body}");
}
