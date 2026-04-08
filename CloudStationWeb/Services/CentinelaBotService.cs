using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Dapper;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace CloudStationWeb.Services
{
    public class BotResponse
    {
        public string Message { get; set; } = "";
        public string? FileUrl { get; set; }
        public string? FileName { get; set; }
        public string? FileType { get; set; }
        public long? FileSize { get; set; }
    }

    /// <summary>
    /// Centinela — AI chatbot for the PIH platform.
    /// Supports Gemini and DeepSeek AI, image commands from Azure Blob, and data commands.
    /// </summary>
    public class CentinelaBotService
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<CentinelaBotService> _logger;
        private readonly string _sqlConn;
        private readonly string _pgConn;
        private readonly string? _geminiApiKey;
        private readonly string _geminiModel;
        private readonly string? _deepSeekApiKey;
        private readonly string _deepSeekModel;
        private readonly string _deepSeekEndpoint;
        private readonly string? _azureOpenAIEndpoint;
        private readonly string? _azureOpenAIKey;
        private readonly string _azureOpenAIDeployment;
        private readonly string _azureOpenAIApiVersion;
        private readonly string? _blobConnectionString;
        private readonly string _blobContainer;
        private readonly ChartService? _chartService;

        // Track per-user Gemini AI mode: userName → active
        private static readonly ConcurrentDictionary<string, bool> _aiModeUsers = new();

        public const string BotUserName = "Centinela";
        public const string BotFullName = "Centinela IA";
        public const string BotUserId = "centinela-bot";
        public const string BotRoom = "centinela";

        // Azure Blob image command mapping: command → (blobName, caption)
        private static readonly Dictionary<string, (string BlobName, string Caption)> ImageCommands = new()
        {
            ["/1"] = ("9c8a7f42-3d91-4e01-a3fa-0d2e5b1c6f7d.png", "📊 Reporte de Unidades actualizado."),
            ["/2"] = ("6f3b2c91-91df-41b6-9a1e-c3f0d0c8e24a.png", "📊 Captura del Power Monitoring."),
            ["/3"] = ("b7e1f9c3-8a2d-4f5d-9c3a-7f1f6e7a2c01.png", "📊 Gráfica de potencia."),
            ["/4"] = ("e1a5f734-9c2e-4b3b-8d5a-6f7e1d2c9b8f.png", "📊 Condición de embalses."),
            ["/5"] = ("d42f3e19-b89c-4f02-90d4-3e7f4a6d2c01.png", "📊 Aportaciones por cuenca propia."),
            ["/6"] = ("reporte_lluvia_1_1_638848218556433423.png", "📊 CFE SPH Grijalva - Reporte de lluvias 24 horas."),
            ["/7"] = ("reporte_lluvia_1_2_638848218556433423.png", "📊 CFE SPH Grijalva - Reporte parcial de lluvias."),
        };

        // Rain report prefixes for dynamic lookup (fallback if exact name not found)
        private static readonly Dictionary<string, string> RainPrefixes = new()
        {
            ["/6"] = "reporte_lluvia_1_1_",
            ["/7"] = "reporte_lluvia_1_2_",
        };

        private const string SystemPrompt = @"Eres Centinela, el asistente de inteligencia artificial de la Plataforma Integral Hidrometeorológica (PIH) de CFE Cuenca Grijalva.

Tu función es ayudar a los operadores e ingenieros con información sobre:
- Estado actual de las 4 presas del Sistema Grijalva: Angostura, Chicoasén (Dr. Manuel Moreno Torres), Malpaso (Netzahualcóyotl) y Peñitas (Ángel Albino Corzo)
- Niveles de almacenamiento, volúmenes y datos de función de vasos
- Generación eléctrica: potencia actual (MW), energía acumulada (MWh), unidades operando, flujo por turbinas
- Pronóstico de lluvias por cuenca y subcuenca (modelo numérico GFS, horizonte ~15 días)
- Precipitación observada en estaciones hidrométricas
- Red telemétrica GOES: estado de transmisiones, estaciones activas/inactivas, tasa de éxito
- Variables en tiempo real: nivel de agua, precipitación, temperatura, presión atmosférica, humedad relativa, viento (velocidad/dirección/ráfaga), voltaje de batería
- Diagnóstico de estaciones: voltaje bajo, falta de transmisión, semáforo VERDE/ROJO
- Información de estaciones específicas: búsqueda por nombre, sensores instalados, lecturas actuales, ubicación, cuenca y subcuenca
- Alertas y umbrales configurados
- Datos históricos de mediciones

Reglas:
- Responde siempre en español
- Sé conciso y profesional
- Usa unidades del sistema (mm para lluvia, msnm para elevaciones, Mm³ para volúmenes, MW para potencia, MWh para energía)
- Si te dan datos del sistema entre [DATOS_SISTEMA], úsalos para responder con precisión
- Cuando tengas pronóstico de lluvia, analiza el riesgo hidrológico: cuencas con mayor acumulado esperado y posible impacto en almacenamiento
- Cuando tengas datos de generación, resume el estado operativo: total del sistema, presas que más generan, y unidades en servicio
- Cuando analices la red telemétrica, identifica: estaciones sin transmitir, voltaje de batería bajo (<11V real), tasa de éxito de transmisiones GOES, y clasifica el estado general de la red
- IMPORTANTE: valores negativos en variables (ej: -13107.2, -1310.7) son errores de decodificación GOES, NO valores reales. Ignóralos o márcalos como 'error de lectura'
- Un voltaje de batería normal es 12-14V. Valores de 0V o negativos indican falla del sensor o estación desconectada
- Cuando te pregunten por una estación específica (ej: 'Cañón del Sumidero', 'Acala', 'Agua Azul'), usa los datos de [DATOS_ESTACION] para responder con: nombre, ubicación, cuenca/subcuenca, sensores instalados y lecturas actuales
- Si el usuario pregunta por una estación y hay múltiples resultados, muestra todas las coincidencias con sus datos principales
- Si no tienes datos suficientes, indícalo claramente
- No inventes datos numéricos

Contexto: Las cuencas son: Angostura (ANG - Río Grijalva-Concordia), Chicoasén/Mezcalapa (MMT - Río Grijalva-Tuxtla Gutiérrez), Malpaso (MPS - Río Grijalva-Villahermosa), Peñitas (PEA - Río Grijalva-Peñitas). El sistema opera en cascada: Angostura → Chicoasén → Malpaso → Peñitas.";

        public CentinelaBotService(IConfiguration config, IHttpClientFactory httpClientFactory, ILogger<CentinelaBotService> logger, ChartService? chartService = null)
        {
            _config = config;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _chartService = chartService;
            _sqlConn = config.GetConnectionString("SqlServer") ?? "";
            _pgConn = config.GetConnectionString("PostgreSQL") ?? "";
            _geminiApiKey = config["Gemini:ApiKey"];
            _geminiModel = config["Gemini:Model"] ?? "gemini-2.0-flash";
            _deepSeekApiKey = config["DeepSeek:ApiKey"];
            _deepSeekModel = config["DeepSeek:Model"] ?? "deepseek-chat";
            _deepSeekEndpoint = config["DeepSeek:Endpoint"] ?? "https://api.deepseek.com/v1";
            _azureOpenAIEndpoint = config["AzureOpenAI:Endpoint"];
            _azureOpenAIKey = config["AzureOpenAI:ApiKey"];
            _azureOpenAIDeployment = config["AzureOpenAI:DeploymentName"] ?? "gpt-4o";
            _azureOpenAIApiVersion = config["AzureOpenAI:ApiVersion"] ?? "2024-10-21";
            _blobConnectionString = config["AzureBlob:ConnectionString"];
            _blobContainer = config["AzureBlob:Container"] ?? "unidades";
        }

        /// <summary>
        /// Process a user message and return the bot's response.
        /// Supports image commands, DeepSeek AI mode, Gemini AI, and command fallback.
        /// </summary>
        public async Task<BotResponse> ProcessMessageAsync(string userMessage, string userName)
        {
            try
            {
                var trimmed = userMessage.Trim();
                var cmd = trimmed.ToLower().Split(' ', 2)[0];

                // Image commands /1 - /7 ALWAYS work (even in AI mode)
                if (ImageCommands.ContainsKey(cmd))
                {
                    return await GetImageCommandResponse(cmd);
                }

                // Chart commands ALWAYS work
                var chartResult = await TryHandleChartCommand(trimmed, cmd);
                if (chartResult != null)
                    return chartResult;

                // /ayuda, /presas, etc. ALWAYS work (even in AI mode)
                if (cmd == "/ayuda" || cmd == "/help" || cmd == "/presas" || cmd == "/vasos"
                    || cmd == "/lluvia" || cmd == "/precipitacion" || cmd == "/precipitación"
                    || cmd == "/estaciones" || cmd == "/alertas" || cmd == "/resumen")
                {
                    return new BotResponse { Message = await ProcessCommandAsync(trimmed, userName) };
                }

                // Activate AI mode /8 (Gemini + DeepSeek fallback)
                if (cmd == "/8")
                {
                    if (string.IsNullOrEmpty(_geminiApiKey) && string.IsNullOrEmpty(_azureOpenAIKey) && string.IsNullOrEmpty(_deepSeekApiKey))
                        return new BotResponse { Message = "⚠️ No hay ninguna IA configurada. Contacta al administrador." };

                    _aiModeUsers[userName] = true;
                    return new BotResponse { Message = "🤖 **Centinela IA activado**\n\nEscribe tu consulta técnica sobre el sistema hidroeléctrico.\nUsa Gemini → Azure OpenAI → DeepSeek (con respaldo automático).\n\n✍️ Escribe **volver** para salir del modo IA." };
                }

                // Check if user wants to exit AI mode
                if (_aiModeUsers.TryGetValue(userName, out var active) && active)
                {
                    if (trimmed.Equals("volver", StringComparison.OrdinalIgnoreCase)
                        || trimmed.Equals("/volver", StringComparison.OrdinalIgnoreCase))
                    {
                        _aiModeUsers[userName] = false;
                        return new BotResponse { Message = "✅ Modo IA desactivado. Volviste al menú de comandos.\n\nEscribe `/ayuda` para ver los comandos disponibles." };
                    }

                    // Process with AI (Gemini → DeepSeek fallback)
                    return new BotResponse { Message = await ProcessWithAIAsync(trimmed, userName) };
                }

                // AI response (if configured and not a command)
                if ((!string.IsNullOrEmpty(_geminiApiKey) || !string.IsNullOrEmpty(_azureOpenAIKey) || !string.IsNullOrEmpty(_deepSeekApiKey)) && !trimmed.StartsWith("/"))
                {
                    var aiResponse = await ProcessWithAIAsync(trimmed, userName);
                    return new BotResponse { Message = aiResponse };
                }

                // Command fallback
                return new BotResponse { Message = await ProcessCommandAsync(trimmed, userName) };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Centinela message from {User}", userName);
                return new BotResponse { Message = "⚠️ Ocurrió un error al procesar tu solicitud. Intenta de nuevo o usa un comando como `/ayuda`." };
            }
        }

        /// <summary>
        /// Check if a message is directed at Centinela (via @mention).
        /// </summary>
        public static bool IsMentioned(string message)
        {
            return message.Contains("@Centinela", StringComparison.OrdinalIgnoreCase)
                || message.Contains("@centinela", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Strip the @Centinela mention from the message.
        /// </summary>
        public static string StripMention(string message)
        {
            return message
                .Replace("@Centinela", "", StringComparison.OrdinalIgnoreCase)
                .Trim();
        }

        #region AI Processing (Gemini → DeepSeek fallback)

        /// <summary>
        /// Process with AI: tries Gemini first, falls back to DeepSeek if Gemini fails,
        /// and returns an error message only if both fail.
        /// </summary>
        private async Task<string> ProcessWithAIAsync(string userMessage, string userName)
        {
            var systemData = await GatherContextDataAsync(userMessage);

            var prompt = !string.IsNullOrEmpty(systemData)
                ? $"[DATOS_SISTEMA]\n{systemData}\n[/DATOS_SISTEMA]\n\nPregunta de {userName}: {userMessage}"
                : $"Pregunta de {userName}: {userMessage}";

            // 1) Try Gemini
            if (!string.IsNullOrEmpty(_geminiApiKey))
            {
                var geminiResult = await CallGeminiAsync(prompt);
                if (geminiResult != null)
                    return geminiResult;

                _logger.LogWarning("Gemini failed, falling back to Azure OpenAI...");
            }

            // 2) Fallback to Azure OpenAI (GPT-4o)
            if (!string.IsNullOrEmpty(_azureOpenAIKey) && !string.IsNullOrEmpty(_azureOpenAIEndpoint))
            {
                var azureResult = await CallAzureOpenAIAsync(prompt);
                if (azureResult != null)
                    return azureResult;

                _logger.LogWarning("Azure OpenAI failed, falling back to DeepSeek...");
            }

            // 3) Fallback to DeepSeek
            if (!string.IsNullOrEmpty(_deepSeekApiKey))
            {
                var deepSeekResult = await CallDeepSeekAsync(prompt);
                if (deepSeekResult != null)
                    return deepSeekResult;

                _logger.LogWarning("DeepSeek also failed.");
            }

            // 4) All failed
            return "⚠️ No pude conectar con ningún servicio de IA (Gemini, Azure OpenAI ni DeepSeek). Posible límite de cuota. Intenta de nuevo en unos segundos o usa comandos como `/ayuda`.";
        }

        private async Task<string?> CallGeminiAsync(string userPrompt)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_geminiModel}:generateContent?key={_geminiApiKey}";

                var body = new
                {
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new[] { new { text = $"{SystemPrompt}\n\n{userPrompt}" } }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.7,
                        maxOutputTokens = 1024,
                        topP = 0.95
                    }
                };

                var json = JsonSerializer.Serialize(body);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(url, content);

                // Retry once on rate-limit (429)
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("Gemini API rate-limited (429), retrying in 3s...");
                    await Task.Delay(3000);
                    response = await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Gemini API returned {Status}: {Body}", response.StatusCode, errorBody.Length > 300 ? errorBody[..300] : errorBody);
                    return null;
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);

                var text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                return text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Gemini API");
                return null;
            }
        }

        private async Task<string?> CallDeepSeekAsync(string userPrompt)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_deepSeekApiKey}");

                var body = new
                {
                    model = _deepSeekModel,
                    messages = new object[]
                    {
                        new { role = "system", content = SystemPrompt },
                        new { role = "user", content = userPrompt }
                    },
                    temperature = 0.7,
                    max_tokens = 1024
                };

                var json = JsonSerializer.Serialize(body);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync($"{_deepSeekEndpoint}/chat/completions", content);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("DeepSeek API returned {Status}: {Body}", response.StatusCode, errorBody.Length > 300 ? errorBody[..300] : errorBody);
                    return null;
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);

                var text = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling DeepSeek API");
                return null;
            }
        }

        private async Task<string?> CallAzureOpenAIAsync(string userPrompt)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();

                string url;
                bool isGitHubModels = _azureOpenAIEndpoint!.Contains("models.inference.ai.azure.com");

                if (isGitHubModels)
                {
                    // GitHub Models: uses Bearer token and /chat/completions
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_azureOpenAIKey}");
                    url = $"{_azureOpenAIEndpoint!.TrimEnd('/')}/chat/completions";
                }
                else
                {
                    // Azure OpenAI: uses api-key header and deployment-based URL
                    client.DefaultRequestHeaders.Add("api-key", _azureOpenAIKey);
                    url = $"{_azureOpenAIEndpoint!.TrimEnd('/')}/openai/deployments/{_azureOpenAIDeployment}/chat/completions?api-version={_azureOpenAIApiVersion}";
                }

                var body = new
                {
                    model = _azureOpenAIDeployment,
                    messages = new object[]
                    {
                        new { role = "system", content = SystemPrompt },
                        new { role = "user", content = userPrompt }
                    },
                    temperature = 0.7,
                    max_tokens = 1024
                };

                var json = JsonSerializer.Serialize(body);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(url, content);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Azure OpenAI returned {Status}: {Body}", response.StatusCode, errorBody.Length > 300 ? errorBody[..300] : errorBody);
                    return null;
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);

                var text = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Azure OpenAI API");
                return null;
            }
        }

        /// <summary>
        /// Gather relevant data from the system based on the user's question intent.
        /// Always provides dam + rain data for general queries.
        /// </summary>
        private async Task<string> GatherContextDataAsync(string userMessage)
        {
            var sb = new StringBuilder();
            var msg = userMessage.ToLower();
            bool fetchedDams = false, fetchedRain = false, fetchedForecast = false, fetchedGeneration = false;

            // Dam/reservoir data
            if (ContainsAny(msg, "presa", "vaso", "almacenamiento", "volumen", "nivel", "elevación",
                "angostura", "chicoasén", "malpaso", "peñitas", "funvasos", "función de vasos",
                "vertedor", "extracción", "aportación", "aportacion"))
            {
                var damData = await GetLatestDamDataAsync();
                if (!string.IsNullOrEmpty(damData))
                    sb.AppendLine("--- Datos de Presas (últimos disponibles) ---\n" + damData);
                fetchedDams = true;
            }

            // Generation data
            if (ContainsAny(msg, "generación", "generacion", "energía", "energia", "potencia",
                "turbina", "unidades", "mw", "mwh", "despacho", "carga"))
            {
                var genData = await GetGenerationSummaryAsync();
                if (!string.IsNullOrEmpty(genData))
                    sb.AppendLine("--- Generación eléctrica ---\n" + genData);
                fetchedGeneration = true;
            }

            // Precipitation data
            if (ContainsAny(msg, "lluvia", "precipitación", "precipitacion", "lloviendo", "llueve", "mm", "pluviómetro"))
            {
                var rainData = await GetRecentPrecipitationAsync();
                if (!string.IsNullOrEmpty(rainData))
                    sb.AppendLine("--- Precipitación reciente (observada) ---\n" + rainData);
                fetchedRain = true;
            }

            // Rain forecast data
            if (ContainsAny(msg, "pronóstico", "pronostico", "forecast", "predicción", "prediccion",
                "previsión", "prevision", "esperada", "va a llover", "lloverá", "próximos días",
                "proximos dias", "mañana", "semana"))
            {
                var forecastData = await GetRainForecastSummaryAsync();
                if (!string.IsNullOrEmpty(forecastData))
                    sb.AppendLine("--- Pronóstico de lluvias por cuenca ---\n" + forecastData);
                fetchedForecast = true;
            }

            // Telemetric network / GOES transmissions
            bool fetchedTelemetric = false;
            if (ContainsAny(msg, "telemétrica", "telemetrica", "telemetría", "telemetria",
                "goes", "transmisión", "transmision", "red", "dcp", "satélite", "satelite",
                "semáforo", "semaforo", "comunicación", "comunicacion"))
            {
                var telData = await GetTelemetricNetworkStatusAsync();
                if (!string.IsNullOrEmpty(telData))
                    sb.AppendLine("--- Red Telemétrica GOES ---\n" + telData);
                var goesData = await GetGOESTransmissionStatusAsync();
                if (!string.IsNullOrEmpty(goesData))
                    sb.AppendLine("--- Transmisiones GOES (24h) ---\n" + goesData);
                fetchedTelemetric = true;
            }

            // Current variable readings
            bool fetchedVariables = false;
            if (ContainsAny(msg, "variable", "nivel de agua", "nivel_de_agua", "temperatura",
                "presión", "presion", "humedad", "viento", "ráfaga", "rafaga",
                "voltaje", "batería", "bateria", "sensor", "mediciones", "lecturas",
                "radiación", "radiacion", "solar"))
            {
                var varData = await GetCurrentVariablesSummaryAsync();
                if (!string.IsNullOrEmpty(varData))
                    sb.AppendLine("--- Variables actuales de la red ---\n" + varData);
                fetchedVariables = true;
            }

            // Station status
            if (ContainsAny(msg, "estación", "estaciones", "funcionando", "activa", "reportando"))
            {
                var stationData = await GetStationStatusSummaryAsync();
                if (!string.IsNullOrEmpty(stationData))
                    sb.AppendLine("--- Estado de estaciones ---\n" + stationData);
                if (!fetchedTelemetric)
                {
                    var telData = await GetTelemetricNetworkStatusAsync();
                    if (!string.IsNullOrEmpty(telData))
                        sb.AppendLine("--- Semáforo Red Telemétrica ---\n" + telData);
                }
            }

            // Alerts
            if (ContainsAny(msg, "alerta", "umbral", "advertencia", "peligro", "riesgo"))
            {
                var alertData = await GetActiveAlertsAsync();
                if (!string.IsNullOrEmpty(alertData))
                    sb.AppendLine("--- Alertas activas ---\n" + alertData);
            }

            // Station-specific search: detect station names or "estación X" patterns
            bool fetchedStation = false;
            var stationSearch = ExtractStationName(userMessage);

            // If regex didn't find a station name, try database fallback
            if (string.IsNullOrEmpty(stationSearch))
                stationSearch = await FindStationNameInDatabaseAsync(userMessage);

            if (!string.IsNullOrEmpty(stationSearch))
            {
                var stationData = await SearchStationByNameAsync(stationSearch);
                if (!string.IsNullOrEmpty(stationData))
                {
                    sb.AppendLine("--- Datos de estación específica ---\n" + stationData);
                    fetchedStation = true;
                }
                else
                {
                    // Regex found something but DB search returned nothing — try DB fallback
                    var fallbackName = await FindStationNameInDatabaseAsync(userMessage);
                    if (!string.IsNullOrEmpty(fallbackName))
                    {
                        stationData = await SearchStationByNameAsync(fallbackName);
                        if (!string.IsNullOrEmpty(stationData))
                        {
                            sb.AppendLine("--- Datos de estación específica ---\n" + stationData);
                            fetchedStation = true;
                        }
                    }
                }
            }

            // For general/vague queries, provide dam + rain + forecast + generation
            // But NOT when a specific station was found (to avoid drowning station data in noise)
            if (!fetchedStation && (sb.Length == 0 || ContainsAny(msg, "resumen", "estado", "general", "cómo está",
                "cómo va", "reporte", "situación", "condición", "condicion", "actual", "hoy",
                "sistema", "grijalva", "cuenca", "todo", "completo", "análisis", "analisis")))
            {
                if (!fetchedDams)
                {
                    var damData = await GetLatestDamDataAsync();
                    if (!string.IsNullOrEmpty(damData)) sb.AppendLine("--- Presas ---\n" + damData);
                }
                if (!fetchedGeneration)
                {
                    var genData = await GetGenerationSummaryAsync();
                    if (!string.IsNullOrEmpty(genData)) sb.AppendLine("--- Generación ---\n" + genData);
                }
                if (!fetchedRain)
                {
                    var rainData = await GetRecentPrecipitationAsync();
                    if (!string.IsNullOrEmpty(rainData)) sb.AppendLine("--- Precipitación ---\n" + rainData);
                }
                if (!fetchedForecast)
                {
                    var forecastData = await GetRainForecastSummaryAsync();
                    if (!string.IsNullOrEmpty(forecastData)) sb.AppendLine("--- Pronóstico lluvias ---\n" + forecastData);
                }
            }

            // When asking specifically about the network, also provide variables
            if (ContainsAny(msg, "red", "telemétrica", "telemetrica", "goes", "transmisión",
                "diagnóstico", "diagnostico"))
            {
                if (!fetchedVariables)
                {
                    var varData = await GetCurrentVariablesSummaryAsync();
                    if (!string.IsNullOrEmpty(varData)) sb.AppendLine("--- Variables actuales ---\n" + varData);
                }
            }

            return sb.ToString();
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            return keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Extract a station name from the user message using pattern detection.
        /// Supports many natural language forms: "estación X", "datos de X", "nivel en X",
        /// "qué reporta X", "X tiene dato", just "X" if it looks like a proper name, etc.
        /// </summary>
        private static string? ExtractStationName(string message)
        {
            var msg = message.Trim();

            // Noise words that are never station names
            var noiseWords = new[] { "sistema", "presas", "cuenca", "general", "red",
                "telemétrica", "telemetrica", "goes", "todas", "todo", "grijalva",
                "pronóstico", "pronostico", "resumen", "reporte", "generación",
                "generacion", "alertas", "ayuda", "hola", "buenos días", "buenas" };

            // Phase 1: Regex patterns (broad set)
            var patterns = new[]
            {
                // "gráfica/grafica de [variable] de [estación]"
                @"gr[aá]fic[ao]?\s+(?:de\s+)?(?:temperatura|precipitaci[oó]n|lluvia|nivel|humedad|presi[oó]n|viento|voltaje)\s+(?:de(?:l)?\s+|en\s+)(.+?)(?:\?|$|,|\.|;)",
                // "estación (GOES/de) X"
                @"estaci[oó]n\s+(?:goes\s+)?(?:de\s+|del\s+)?(.+?)(?:\?|$|,|\.|;)",
                // "nivel/datos/sensores/lecturas/info de (la/las/el) (estación) X"
                @"(?:nivel|dato|datos|sensores?|lecturas?|informaci[oó]n|info|mediciones?|variables?)\s+(?:de\s+(?:la\s+|las\s+|el\s+)?)?(?:estaci[oó]n\s+)?(?:goes\s+)?(.+?)(?:\?|$|,|\.|;)",
                // "cómo está/va (la estación) X"
                @"(?:c[oó]mo|que)\s+(?:est[aá]|va|reporta|mide|tiene)\s+(?:la\s+)?(?:estaci[oó]n\s+)?(.+?)(?:\?|$|,|\.|;)",
                // "qué reporta/mide/hay en X"
                @"qu[eé]\s+(?:reporta|mide|hay|tiene|pasa|sucede)\s+(?:en\s+|la\s+)?(?:estaci[oó]n\s+)?(.+?)(?:\?|$|,|\.|;)",
                // "llueve/llovió/lluvia en X"
                @"(?:llueve|llovi[oó]|lluvia|precipit[aó])\s+(?:en\s+)?(.+?)(?:\?|$|,|\.|;)",
                // "temperatura/presión/humedad en X"
                @"(?:temperatura|presi[oó]n|humedad|viento|nivel)\s+(?:en|de)\s+(?:la\s+)?(.+?)(?:\?|$|,|\.|;)",
                // "reporta/transmite X"
                @"(?:reporta|transmite|funciona)\s+(?:la\s+)?(?:estaci[oó]n\s+)?(.+?)(?:\?|$|,|\.|;)",
                // "dame/dime/muestra (los datos de) X"
                @"(?:dame|dime|muestra|mu[eé]strame|necesito|quiero)\s+(?:los\s+)?(?:datos?\s+)?(?:de\s+(?:la\s+|las\s+|el\s+)?)?(?:estaci[oó]n\s+)?(.+?)(?:\?|$|,|\.|;)",
                // "X nivel/dato/sensor" (station name first)
                @"^(.+?)\s+(?:nivel|dato|datos|sensor|sensores|lectura|lecturas|estado|reporte)(?:\?|$|,|\.|;|\s)"
            };

            foreach (var pattern in patterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(msg, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var name = match.Groups[1].Value.Trim();
                    // Remove trailing noise
                    name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+(actual|ahora|hoy|últimas?|ultima|reciente|mente|por favor)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                    // Strip variable/measurement prefixes that get accidentally captured
                    var varPrefixes = new[]
                    {
                        @"^(?:nivel\s+de\s+agua\s+(?:de(?:l)?\s+)?)",
                        @"^(?:nivel\s+(?:de(?:l)?\s+|en\s+)?)",
                        @"^(?:precipitaci[oó]n\s+(?:de(?:l)?\s+|en\s+)?)",
                        @"^(?:lluvia\s+(?:de(?:l)?\s+|en\s+)?)",
                        @"^(?:temperatura\s+(?:de(?:l)?\s+|en\s+)?)",
                        @"^(?:presi[oó]n\s+(?:de(?:l)?\s+|en\s+)?)",
                        @"^(?:humedad\s+(?:de(?:l)?\s+|en\s+)?)",
                        @"^(?:voltaje\s+(?:de(?:l)?\s+)?)",
                        @"^(?:bater[ií]a\s+(?:de(?:l)?\s+)?)",
                        @"^(?:viento\s+(?:de(?:l)?\s+|en\s+)?)",
                        @"^(?:agua\s+(?:de(?:l)?\s+)?)"
                    };
                    foreach (var prefix in varPrefixes)
                        name = System.Text.RegularExpressions.Regex.Replace(name, prefix, "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                    if (name.Length >= 3 && !noiseWords.Any(n => name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                        return name;
                }
            }

            return null;
        }

        /// <summary>
        /// Fallback: search the database directly for any part of the user message
        /// that matches a station name. Used when ExtractStationName returns null.
        /// </summary>
        private async Task<string?> FindStationNameInDatabaseAsync(string userMessage)
        {
            try
            {
                // Clean the message: remove common filler words to get potential station names
                var msg = userMessage.Trim();
                var fillers = new[] { "qué", "que", "cuál", "cual", "cómo", "como", "dónde",
                    "donde", "está", "esta", "hay", "tiene", "dame", "dime", "muestra",
                    "ver", "consulta", "busca", "nivel", "dato", "datos", "sensor",
                    "sensores", "lectura", "lecturas", "estación", "estacion",
                    "información", "informacion", "info", "reporte", "reporta",
                    "actual", "ahora", "hoy", "del", "de", "la", "las", "el", "los",
                    "en", "por", "para", "con", "goes", "medición", "medicion", "agua",
                    "precipitación", "precipitacion", "lluvia", "temperatura", "presión",
                    "presion", "humedad", "viento", "voltaje", "batería", "bateria",
                    "variable", "variables", "favor",
                    "gráfica", "grafica", "graficar", "grafique", "grafícame", "graficame",
                    "gráfico", "grafico", "tendencia", "evolución", "evolucion",
                    "chart", "dibuja", "plotea", "muéstrame", "muestrame" };

                // Build search terms: try the full message, then progressively shorter segments
                var searchTerms = new List<string>();

                // Try the full message minus common filler words
                var words = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var meaningful = words.Where(w => !fillers.Contains(w.ToLower().TrimEnd('?', '.', ',', ';'))).ToArray();
                if (meaningful.Length >= 1 && meaningful.Length <= 5)
                    searchTerms.Add(string.Join(" ", meaningful));

                // Try consecutive word pairs and triples from the original message
                for (int i = 0; i < words.Length; i++)
                {
                    var w = words[i].TrimEnd('?', '.', ',', ';');
                    if (w.Length >= 3 && !fillers.Contains(w.ToLower()))
                    {
                        // Single meaningful word
                        if (w.Length >= 4)
                            searchTerms.Add(w);
                        // Word pair
                        if (i + 1 < words.Length)
                        {
                            var w2 = words[i + 1].TrimEnd('?', '.', ',', ';');
                            searchTerms.Add($"{w} {w2}");
                        }
                        // Word triple
                        if (i + 2 < words.Length)
                        {
                            var w2 = words[i + 1].TrimEnd('?', '.', ',', ';');
                            var w3 = words[i + 2].TrimEnd('?', '.', ',', ';');
                            searchTerms.Add($"{w} {w2} {w3}");
                        }
                    }
                }

                if (!searchTerms.Any()) return null;

                using var sqlDb = new SqlConnection(_sqlConn);
                foreach (var term in searchTerms)
                {
                    if (term.Length < 3) continue;
                    var count = await sqlDb.QueryFirstOrDefaultAsync<int>(
                        "SELECT COUNT(*) FROM NV_Estacion WHERE Nombre LIKE @S AND Activo = 1",
                        new { S = $"%{term}%" });
                    if (count > 0 && count <= 10)
                        return term;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in FindStationNameInDatabaseAsync");
                return null;
            }
        }

        #endregion

        // DeepSeek processing is now handled by CallDeepSeekAsync in the AI Processing region above

        #region Azure Blob Image Commands

        private async Task<BotResponse> GetImageCommandResponse(string command)
        {
            try
            {
                if (string.IsNullOrEmpty(_blobConnectionString))
                    return new BotResponse { Message = "⚠️ Azure Blob Storage no está configurado." };

                var (blobName, caption) = ImageCommands[command];
                var sasUrl = await GetBlobSasUrlAsync(blobName, command);

                if (sasUrl == null)
                    return new BotResponse { Message = $"⚠️ No se encontró la imagen para el comando `{command}`." };

                return new BotResponse
                {
                    Message = caption,
                    FileUrl = sasUrl,
                    FileName = blobName,
                    FileType = "image/png"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting blob image for command {Command}", command);
                return new BotResponse { Message = $"⚠️ Error al obtener la imagen del comando `{command}`." };
            }
        }

        private async Task<string?> GetBlobSasUrlAsync(string blobName, string command)
        {
            var blobServiceClient = new BlobServiceClient(_blobConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(_blobContainer);

            var blobClient = containerClient.GetBlobClient(blobName);

            // Check if exact blob exists
            if (await blobClient.ExistsAsync())
            {
                return GenerateSasUrl(blobClient);
            }

            // For rain reports, try prefix-based search (name may change with timestamp)
            if (RainPrefixes.TryGetValue(command, out var prefix))
            {
                await foreach (var blob in containerClient.GetBlobsAsync(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, prefix, default))
                {
                    var fallbackClient = containerClient.GetBlobClient(blob.Name);
                    return GenerateSasUrl(fallbackClient);
                }
            }

            return null;
        }

        private static string GenerateSasUrl(BlobClient blobClient)
        {
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = blobClient.BlobContainerName,
                BlobName = blobClient.Name,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var sasUri = blobClient.GenerateSasUri(sasBuilder);
            return sasUri.ToString();
        }

        #endregion

        #region Chart Commands

        private async Task<BotResponse?> TryHandleChartCommand(string message, string cmd)
        {
            if (_chartService == null) return null;

            var lower = message.ToLower();
            var parts = message.Split(' ', 2);
            var arg = parts.Length > 1 ? parts[1].Trim() : "";

            // Slash commands for charts
            if (cmd == "/grafica" || cmd == "/gráfica" || cmd == "/chart")
            {
                return await RouteChartRequest(arg.ToLower());
            }

            // Natural language chart detection (only if message contains chart-related keywords)
            if (ContainsAny(lower, "gráfica", "grafica", "graficar", "grafique", "grafícame",
                "tendencia", "evolución", "evolucion", "chart", "gráfico", "grafico",
                "dibuja", "plotea", "muestra la gráfica", "muestra la grafica"))
            {
                return await RouteChartRequest(lower);
            }

            return null;
        }

        private async Task<BotResponse> RouteChartRequest(string context)
        {
            if (_chartService == null)
                return new BotResponse { Message = "⚠️ El servicio de gráficas no está disponible." };

            // Extract variable filter if present
            var varFilter = ExtractVariableFilter(context);

            // Try to detect a station name in the request (regex first, then DB fallback)
            var stationName = ExtractStationName(context);
            if (stationName == null)
                stationName = await FindStationNameInDatabaseAsync(context);
            if (stationName != null)
            {
                var dcpId = await FindDcpIdForStation(stationName);
                if (dcpId != null)
                    return await _chartService.GenerateStationChartAsync(dcpId, stationName, varFilter);
                else
                    return new BotResponse { Message = $"⚠️ Encontré la estación '{stationName}' pero no tiene DCP GOES asignado para obtener datos de gráfica." };
            }

            // Determine chart type from context keywords
            if (ContainsAny(context, "generación", "generacion", "mw", "megawatt", "potencia generada"))
            {
                var presa = ExtractPresaFilter(context);
                return await _chartService.GenerateGenerationChartAsync(presa);
            }

            if (ContainsAny(context, "elevación", "elevacion", "embalse", "almacenamiento", "msnm"))
            {
                var presa = ExtractPresaFilter(context);
                return await _chartService.GenerateDamChartAsync(presa);
            }

            // "cuenca del grijalva", "cascada", "vasos", "funvasos" → dam cascade chart (5 points)
            if (ContainsAny(context, "cascada", "funvasos", "funcionamiento de vasos"))
            {
                return await _chartService.GenerateDamChartAsync(null);
            }

            if (ContainsAny(context, "cuenca") && ContainsAny(context, "grijalva", "cascada", "vaso", "presa", "embalse"))
            {
                return await _chartService.GenerateDamChartAsync(null);
            }

            // "precipitación por cuenca" / "lluvia por cuenca" / "cuenca" sola → cuenca precipitation
            if (ContainsAny(context, "cuenca", "subcuenca"))
            {
                if (ContainsAny(context, "lluvia", "precipitación", "precipitacion", "pluviom"))
                    return await _chartService.GenerateCuencaPrecipChartAsync();
                // "cuenca" sola → cuenca precipitation by default
                return await _chartService.GenerateCuencaPrecipChartAsync();
            }

            if (ContainsAny(context, "lluvia", "precipitación", "precipitacion", "lloviendo", "pluviom"))
            {
                return await _chartService.GeneratePrecipitationChartAsync();
            }

            if (ContainsAny(context, "presa", "vaso", "presas", "vasos"))
            {
                var presa = ExtractPresaFilter(context);
                return await _chartService.GenerateDamChartAsync(presa);
            }

            if (ContainsAny(context, "estación", "estacion", "sensor", "station"))
            {
                return new BotResponse { Message = "⚠️ Especifica la estación. Ejemplo: `/grafica estación Peñitas`\no \"gráfica de temperatura de Aza-Pac\"" };
            }

            // Default: show dam elevation chart
            return await _chartService.GenerateDamChartAsync();
        }

        private static string? ExtractVariableFilter(string text)
        {
            if (ContainsAny(text, "temperatura")) return "temperatura";
            if (ContainsAny(text, "precipitación", "precipitacion", "lluvia")) return "precipitación";
            if (ContainsAny(text, "nivel", "nivel de agua")) return "nivel_de_agua";
            if (ContainsAny(text, "humedad")) return "humedad";
            if (ContainsAny(text, "viento")) return "viento";
            if (ContainsAny(text, "presión", "presion", "barométr")) return "presión";
            if (ContainsAny(text, "voltaje", "batería", "bateria")) return "voltaje";
            if (ContainsAny(text, "radiación", "radiacion", "solar")) return "radiación";
            return null;
        }

        private static string? ExtractPresaFilter(string text)
        {
            if (ContainsAny(text, "angostura", "ang")) return "angostura";
            if (ContainsAny(text, "chicoasén", "chicoasen", "mmt", "mezcalapa")) return "chicoasén";
            if (ContainsAny(text, "malpaso", "mps")) return "malpaso";
            if (ContainsAny(text, "peñitas", "penitas", "pea")) return "peñitas";
            return null;
        }

        private async Task<string?> FindDcpIdForStation(string stationName)
        {
            try
            {
                using var db = new Microsoft.Data.SqlClient.SqlConnection(_sqlConn);
                // Use NV_Estacion + DatosGOES (same pattern as SearchStationByNameAsync)
                var result = await Dapper.SqlMapper.QueryFirstOrDefaultAsync<string>(db, @"
                    SELECT TOP 1 dg.IdSatelital
                    FROM NV_Estacion e
                    LEFT JOIN DatosGOES dg ON dg.IdEstacion = e.Id
                    WHERE e.Activo = 1
                      AND (e.Nombre LIKE @Name OR e.IdAsignado LIKE @Name OR e.Etiqueta LIKE @Name)
                      AND dg.IdSatelital IS NOT NULL",
                    new { Name = $"%{stationName}%" });
                return result?.Trim();
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Command Fallback

        private async Task<string> ProcessCommandAsync(string message, string userName)
        {
            var trimmed = message.Trim().ToLower();

            // Command-based responses
            if (trimmed.StartsWith("/"))
            {
                var parts = trimmed.Split(' ', 2);
                var cmd = parts[0];
                var arg = parts.Length > 1 ? parts[1] : "";

                return cmd switch
                {
                    "/ayuda" or "/help" => GetHelpText(),
                    "/presas" or "/vasos" => await GetDamSummaryCommand(),
                    "/lluvia" or "/precipitacion" or "/precipitación" => await GetRainSummaryCommand(arg),
                    "/estaciones" => await GetStationSummaryCommand(),
                    "/alertas" => await GetAlertSummaryCommand(),
                    "/resumen" => await GetGeneralSummaryCommand(),
                    _ => $"Comando no reconocido: `{cmd}`. Escribe `/ayuda` para ver los comandos disponibles."
                };
            }

            // Natural language fallback (no Gemini available)
            if (ContainsAny(trimmed, "presa", "vaso", "almacenamiento"))
                return await GetDamSummaryCommand();
            if (ContainsAny(trimmed, "lluvia", "precipitación", "lloviendo"))
                return await GetRainSummaryCommand("");
            if (ContainsAny(trimmed, "hola", "buenos días", "buenas tardes", "buenas noches"))
                return $"¡Hola {userName}! Soy Centinela, el asistente de la PIH. Escribe `/ayuda` para ver lo que puedo hacer.";

            return $"Hola {userName}, no tengo la IA activa en este momento por lo que funciono con comandos. Escribe `/ayuda` para ver los comandos disponibles.";
        }

        private string GetHelpText()
        {
            return @"🤖 **Centinela — Comandos disponibles**

📊 **Reportes con imagen:**
`/1` — Unidades conectadas
`/2` — Power Monitoring
`/3` — Gráfica de potencia
`/4` — Condición de embalses
`/5` — Aportaciones por cuenca propia
`/6` — Reporte de lluvias 24 horas
`/7` — Reporte parcial de lluvias

🤖 **Inteligencia Artificial:**
`/8` — Activar IA (Gemini + DeepSeek) — consultas en lenguaje natural

� **Gráficas dinámicas:**
`/grafica presas` — Evolución de elevación de embalses
`/grafica generacion` — Generación eléctrica (MW)
`/grafica lluvia` — Top 15 precipitación 24h
`/grafica cuencas` — Precipitación por cuenca
`/grafica estación [nombre]` — Sensores de una estación

�📋 **Datos del sistema:**
`/resumen` — Resumen general del sistema
`/presas` — Estado actual de las 4 presas
`/lluvia` — Precipitación reciente en estaciones
`/lluvia [cuenca]` — Precipitación filtrada por cuenca
`/estaciones` — Estado de las estaciones
`/alertas` — Alertas activas del sistema
`/ayuda` — Mostrar esta ayuda

También puedes mencionarme con **@Centinela** en cualquier sala.";
        }

        private async Task<string> GetDamSummaryCommand()
        {
            var data = await GetLatestDamDataAsync();
            return string.IsNullOrEmpty(data)
                ? "No se encontraron datos recientes de presas."
                : $"🏔️ **Estado de Presas**\n\n{data}";
        }

        private async Task<string> GetRainSummaryCommand(string filter)
        {
            var data = await GetRecentPrecipitationAsync(filter);
            return string.IsNullOrEmpty(data)
                ? "No se encontraron datos recientes de precipitación."
                : $"🌧️ **Precipitación reciente**\n\n{data}";
        }

        private async Task<string> GetStationSummaryCommand()
        {
            var data = await GetStationStatusSummaryAsync();
            return string.IsNullOrEmpty(data)
                ? "No se pudo obtener el estado de las estaciones."
                : $"📡 **Estado de estaciones**\n\n{data}";
        }

        private async Task<string> GetAlertSummaryCommand()
        {
            var data = await GetActiveAlertsAsync();
            return string.IsNullOrEmpty(data)
                ? "No hay alertas activas en este momento. ✅"
                : $"⚠️ **Alertas activas**\n\n{data}";
        }

        private async Task<string> GetGeneralSummaryCommand()
        {
            var sb = new StringBuilder();
            sb.AppendLine("📊 **Resumen General del Sistema**\n");

            var dams = await GetLatestDamDataAsync();
            if (!string.IsNullOrEmpty(dams))
            {
                sb.AppendLine("🏔️ **Presas:**");
                sb.AppendLine(dams);
                sb.AppendLine();
            }

            var gen = await GetGenerationSummaryAsync();
            if (!string.IsNullOrEmpty(gen))
            {
                sb.AppendLine("⚡ **Generación:**");
                sb.AppendLine(gen);
                sb.AppendLine();
            }

            var rain = await GetRecentPrecipitationAsync();
            if (!string.IsNullOrEmpty(rain))
            {
                sb.AppendLine("🌧️ **Precipitación:**");
                sb.AppendLine(rain);
                sb.AppendLine();
            }

            var forecast = await GetRainForecastSummaryAsync();
            if (!string.IsNullOrEmpty(forecast))
            {
                sb.AppendLine("🔮 **Pronóstico de lluvias:**");
                sb.AppendLine(forecast);
                sb.AppendLine();
            }

            var telemetric = await GetTelemetricNetworkStatusAsync();
            if (!string.IsNullOrEmpty(telemetric))
            {
                sb.AppendLine("📡 **Red Telemétrica:**");
                sb.AppendLine(telemetric);
                sb.AppendLine();
            }

            var alerts = await GetActiveAlertsAsync();
            if (!string.IsNullOrEmpty(alerts))
            {
                sb.AppendLine("⚠️ **Alertas:**");
                sb.AppendLine(alerts);
            }
            else
            {
                sb.AppendLine("✅ Sin alertas activas.");
            }

            return sb.ToString();
        }

        #endregion

        #region Data Access Methods

        private async Task<string> GetLatestDamDataAsync()
        {
            try
            {
                using var db = new NpgsqlConnection(_pgConn);
                // Get latest funvasos data per dam from funvasos_horario
                var rows = await db.QueryAsync<dynamic>(@"
                    SELECT presa, ts, hora,
                           elevacion, almacenamiento, diferencia,
                           aportaciones_q, aportaciones_v,
                           extracciones_turb_q, extracciones_total_q,
                           extracciones_vert_q,
                           generacion, num_unidades,
                           aportacion_cuenca_propia
                    FROM funvasos_horario
                    WHERE ts = (SELECT MAX(ts) FROM funvasos_horario)
                    ORDER BY presa, hora DESC");

                var list = rows.ToList();
                if (!list.Any()) return "";

                var sb = new StringBuilder();
                var grouped = list.GroupBy(r => (string)r.presa);

                foreach (var g in grouped)
                {
                    var latest = g.First();
                    sb.AppendLine($"**{g.Key}** (fecha {((DateTime)latest.ts):dd/MMM/yyyy}, hora {latest.hora}):");

                    if (latest.elevacion != null)
                        sb.AppendLine($"  Elevación: {latest.elevacion:F2} msnm");
                    if (latest.almacenamiento != null)
                        sb.AppendLine($"  Almacenamiento: {latest.almacenamiento:F2} Mm³");
                    if (latest.diferencia != null && (float)latest.diferencia != 0)
                        sb.AppendLine($"  Diferencia: {latest.diferencia:F2} Mm³");
                    if (latest.aportaciones_q != null)
                        sb.AppendLine($"  Aportaciones: {latest.aportaciones_q:F2} m³/s");
                    if (latest.extracciones_total_q != null)
                        sb.AppendLine($"  Extracciones totales: {latest.extracciones_total_q:F2} m³/s");
                    if (latest.extracciones_turb_q != null)
                        sb.AppendLine($"  Extracción turbinas: {latest.extracciones_turb_q:F2} m³/s");
                    if (latest.extracciones_vert_q != null && (float)latest.extracciones_vert_q != 0)
                        sb.AppendLine($"  Vertedor: {latest.extracciones_vert_q:F2} m³/s");
                    if (latest.generacion != null)
                        sb.AppendLine($"  Generación: {latest.generacion:F2} MW ({latest.num_unidades} unidades)");
                    sb.AppendLine();
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dam data for Centinela");
                return "";
            }
        }

        private async Task<string> GetRecentPrecipitationAsync(string? cuencaFilter = null)
        {
            try
            {
                // Get station names from SQL Server (NV_Estacion has pre-joined cuenca/subcuenca)
                List<dynamic>? stationList = null;
                using (var sqlDb = new SqlConnection(_sqlConn))
                {
                    var query = @"
                        SELECT IdAsignado, Nombre, NombreCuenca, NombreSubcuenca 
                        FROM NV_Estacion 
                        WHERE Visible = 1 AND Activo = 1";

                    if (!string.IsNullOrEmpty(cuencaFilter))
                    {
                        query += " AND NombreCuenca LIKE @Filter";
                        stationList = (await sqlDb.QueryAsync<dynamic>(query, new { Filter = $"%{cuencaFilter}%" })).ToList();
                    }
                    else
                    {
                        stationList = (await sqlDb.QueryAsync<dynamic>(query)).ToList();
                    }
                }

                var sb = new StringBuilder();

                // 1) lluvia_acumulada — 24h accumulated rain (most useful)
                using var pgDb = new NpgsqlConnection(_pgConn);
                var acumulada = await pgDb.QueryAsync<dynamic>(@"
                    SELECT id_asignado, dcp_id, acumulado, periodo_inicio, periodo_fin, horas_con_dato
                    FROM lluvia_acumulada
                    WHERE tipo_periodo = '24h'
                      AND acumulado > 0
                    ORDER BY acumulado DESC
                    LIMIT 20");

                var acumList = acumulada.ToList();
                if (acumList.Any())
                {
                    sb.AppendLine("**Lluvia acumulada 24h (top estaciones):**");
                    foreach (var a in acumList)
                    {
                        var station = stationList?.FirstOrDefault(s =>
                            s.IdAsignado != null && (string)s.IdAsignado == (string)(a.id_asignado ?? a.dcp_id));
                        var name = station != null ? (string)station.Nombre : (string)(a.id_asignado ?? a.dcp_id);
                        var cuenca = station != null ? $" ({station.NombreCuenca})" : "";
                        sb.AppendLine($"• {name}{cuenca}: **{a.acumulado:F1} mm** ({a.horas_con_dato}h reportadas)");
                    }
                }

                // 2) precipitacion_cuenca — por cuenca summary
                var cuencas = await pgDb.QueryAsync<dynamic>(@"
                    SELECT nombre, promedio_mm, max_mm, estaciones_con_dato, estaciones_total, semaforo
                    FROM precipitacion_cuenca
                    WHERE ts = (SELECT MAX(ts) FROM precipitacion_cuenca)
                      AND tipo = 'cuenca'
                    ORDER BY promedio_mm DESC");

                var cuencaList = cuencas.ToList();
                if (cuencaList.Any())
                {
                    sb.AppendLine("\n**Precipitación promedio por cuenca (última hora):**");
                    foreach (var c in cuencaList)
                    {
                        var semaforo = (string)c.semaforo == "verde" ? "🟢" : (string)c.semaforo == "amarillo" ? "🟡" : "🔴";
                        sb.AppendLine($"• {semaforo} {c.nombre}: prom {c.promedio_mm:F2} mm, máx {c.max_mm:F1} mm ({c.estaciones_con_dato}/{c.estaciones_total} estaciones)");
                    }
                }

                if (sb.Length == 0)
                    return "Sin precipitación significativa reciente.";

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting precipitation data for Centinela");
                return "";
            }
        }

        private async Task<string> GetStationStatusSummaryAsync()
        {
            try
            {
                using var sqlDb = new SqlConnection(_sqlConn);
                var stats = await sqlDb.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT 
                        COUNT(*) as Total,
                        SUM(CASE WHEN Activo = 1 THEN 1 ELSE 0 END) as Activas,
                        SUM(CASE WHEN Visible = 1 THEN 1 ELSE 0 END) as Visibles
                    FROM Estacion");

                using var pgDb = new NpgsqlConnection(_pgConn);
                var reporting = await pgDb.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT 
                        COUNT(DISTINCT dcp_id) as Reportando,
                        MIN(ts) as PrimerReporte,
                        MAX(ts) as UltimoReporte
                    FROM ultimas_mediciones
                    WHERE ts > NOW() - INTERVAL '1 hour'");

                // Check maintenance
                var maintenance = await sqlDb.QueryAsync<dynamic>(@"
                    SELECT e.Nombre, m.Motivo
                    FROM MaintenanceWindows m
                    INNER JOIN Estacion e ON e.IdAsignado = m.StationId
                    WHERE m.IsActive = 1 AND GETUTCDATE() BETWEEN m.StartDate AND m.EndDate");

                var maintList = maintenance.ToList();

                var sb = new StringBuilder();
                sb.AppendLine($"Total estaciones: {stats?.Total ?? 0}");
                sb.AppendLine($"Activas: {stats?.Activas ?? 0}");
                sb.AppendLine($"Reportando última hora: {reporting?.Reportando ?? 0}");

                if (maintList.Any())
                {
                    sb.AppendLine($"\n🔧 En mantenimiento ({maintList.Count}):");
                    foreach (var m in maintList.Take(10))
                        sb.AppendLine($"  • {m.Nombre}: {m.Motivo}");
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting station status for Centinela");
                return "";
            }
        }

        private async Task<string> GetRainForecastSummaryAsync()
        {
            try
            {
                using var db = new NpgsqlConnection(_pgConn);

                // Get latest forecast metadata
                var forecastInfo = await db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT forecast_date, model_run, record_count
                    FROM rain_forecast.forecast
                    ORDER BY forecast_date DESC LIMIT 1");

                if (forecastInfo == null) return "";

                DateTime forecastDate = forecastInfo.forecast_date;

                // Aggregate by cuenca: next 24h, 48h, 72h and total
                var cuencaData = await db.QueryAsync<dynamic>(@"
                    SELECT cuenca_code,
                           SUM(CASE WHEN ts < NOW() + INTERVAL '24 hours' THEN lluvia_media_mm ELSE 0 END) as lluvia_24h,
                           SUM(CASE WHEN ts < NOW() + INTERVAL '48 hours' THEN lluvia_media_mm ELSE 0 END) as lluvia_48h,
                           SUM(CASE WHEN ts < NOW() + INTERVAL '72 hours' THEN lluvia_media_mm ELSE 0 END) as lluvia_72h,
                           SUM(lluvia_media_mm) as lluvia_total,
                           MAX(lluvia_max_mm) as lluvia_max_punto,
                           COUNT(DISTINCT subcuenca_name) as subcuencas
                    FROM rain_forecast.resumen_horario_pronostico
                    WHERE forecast_date = @ForecastDate
                    GROUP BY cuenca_code
                    ORDER BY lluvia_total DESC", new { ForecastDate = forecastDate });

                var cuencaList = cuencaData.ToList();
                if (!cuencaList.Any()) return "";

                var cuencaNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "ANG", "Angostura" }, { "MMT", "Chicoasén (Mezcalapa)" },
                    { "MPS", "Malpaso" }, { "PEA", "Peñitas" }
                };

                var sb = new StringBuilder();
                sb.AppendLine($"Modelo: GFS {forecastInfo.model_run} del {forecastDate:dd/MMM/yyyy}");
                sb.AppendLine();

                foreach (var c in cuencaList)
                {
                    var name = cuencaNames.GetValueOrDefault((string)c.cuenca_code, (string)c.cuenca_code);
                    sb.AppendLine($"**{name}** ({c.subcuencas} subcuencas):");
                    sb.AppendLine($"  Próx 24h: {c.lluvia_24h:F1} mm | 48h: {c.lluvia_48h:F1} mm | 72h: {c.lluvia_72h:F1} mm | Total: {c.lluvia_total:F1} mm");
                    sb.AppendLine($"  Máx puntual: {c.lluvia_max_punto:F1} mm/h");
                }

                // Top subcuencas with most predicted rain
                var topSub = await db.QueryAsync<dynamic>(@"
                    SELECT cuenca_code, subcuenca_name,
                           SUM(lluvia_media_mm) as lluvia_total,
                           MAX(lluvia_max_mm) as lluvia_max
                    FROM rain_forecast.resumen_horario_pronostico
                    WHERE forecast_date = @ForecastDate
                    GROUP BY cuenca_code, subcuenca_name
                    ORDER BY lluvia_total DESC
                    LIMIT 10", new { ForecastDate = forecastDate });

                var topList = topSub.ToList();
                if (topList.Any())
                {
                    sb.AppendLine("\n**Top subcuencas (mayor lluvia pronosticada):**");
                    foreach (var s in topList)
                    {
                        var cuencaName = cuencaNames.GetValueOrDefault((string)s.cuenca_code, (string)s.cuenca_code);
                        sb.AppendLine($"• {s.subcuenca_name} ({cuencaName}): {s.lluvia_total:F1} mm total, máx {s.lluvia_max:F1} mm/h");
                    }
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting rain forecast data for Centinela");
                return "";
            }
        }

        private async Task<string> GetGenerationSummaryAsync()
        {
            try
            {
                using var db = new NpgsqlConnection(_pgConn);

                var rows = await db.QueryAsync<dynamic>(@"
                    SELECT presa, ts, hora, generacion, num_unidades,
                           extracciones_turb_q, extracciones_vert_q
                    FROM funvasos_horario
                    WHERE ts = (SELECT MAX(ts) FROM funvasos_horario)
                      AND generacion IS NOT NULL
                    ORDER BY presa, hora DESC");

                var list = rows.ToList();
                if (!list.Any()) return "";

                var sb = new StringBuilder();
                var grouped = list.GroupBy(r => (string)r.presa);
                double totalActual = 0;
                double totalEnergia = 0;
                DateTime? fecha = null;

                foreach (var g in grouped)
                {
                    var latest = g.First();
                    fecha ??= latest.ts;
                    var genActual = (double?)latest.generacion ?? 0;
                    var unidades = latest.num_unidades;
                    var genMax = g.Max(r => (double?)r.generacion ?? 0);
                    var genProm = g.Average(r => (double?)r.generacion ?? 0);
                    var genTotal = g.Sum(r => (double?)r.generacion ?? 0);
                    totalActual += genActual;
                    totalEnergia += genTotal;

                    sb.AppendLine($"**{g.Key}** (hora {latest.hora}):");
                    sb.AppendLine($"  Actual: {genActual:F1} MW ({unidades} unidades) | Prom: {genProm:F1} MW | Máx: {genMax:F1} MW | Energía: {genTotal:F0} MWh");
                    if (latest.extracciones_turb_q != null)
                        sb.Append($"  Turbinas: {latest.extracciones_turb_q:F1} m³/s");
                    if (latest.extracciones_vert_q != null && (double)latest.extracciones_vert_q != 0)
                        sb.Append($" | Vertedor: {latest.extracciones_vert_q:F1} m³/s");
                    sb.AppendLine();
                }

                if (fecha.HasValue)
                    sb.AppendLine($"\nFecha datos: {fecha.Value:dd/MMM/yyyy}");
                sb.AppendLine($"**TOTAL SISTEMA:** {totalActual:F1} MW actual | {totalEnergia:F0} MWh energía del día");

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting generation data for Centinela");
                return "";
            }
        }

        private async Task<string> GetTelemetricNetworkStatusAsync()
        {
            try
            {
                using var db = new NpgsqlConnection(_pgConn);

                // Semaphore from estatus_estaciones
                var semaforo = await db.QueryAsync<dynamic>(@"
                    SELECT color_estatus, COUNT(*) as cantidad
                    FROM estatus_estaciones
                    GROUP BY color_estatus
                    ORDER BY color_estatus");

                var sb = new StringBuilder();
                var semList = semaforo.ToList();
                if (semList.Any())
                {
                    sb.AppendLine("**Semáforo de estaciones:**");
                    int total = 0;
                    foreach (var s in semList)
                    {
                        var icon = ((string)s.color_estatus) == "VERDE" ? "🟢" : ((string)s.color_estatus) == "ROJO" ? "🔴" : "🟡";
                        sb.AppendLine($"  {icon} {s.color_estatus}: {s.cantidad} estaciones");
                        total += (int)(long)s.cantidad;
                    }
                    sb.AppendLine($"  Total en red: {total}");
                }

                // Stations reporting in last hour vs last 4h
                var reporting = await db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT 
                        COUNT(DISTINCT CASE WHEN ts > NOW() - INTERVAL '1 hour' THEN dcp_id END) as ultima_hora,
                        COUNT(DISTINCT CASE WHEN ts > NOW() - INTERVAL '4 hours' THEN dcp_id END) as ultimas_4h,
                        COUNT(DISTINCT dcp_id) as total_con_dato,
                        MAX(ts) as ultimo_reporte
                    FROM ultimas_mediciones");

                if (reporting != null)
                {
                    sb.AppendLine($"\n**Actividad de reporte:**");
                    sb.AppendLine($"  Reportando última hora: {reporting.ultima_hora}");
                    sb.AppendLine($"  Reportando últimas 4h: {reporting.ultimas_4h}");
                    sb.AppendLine($"  Total con lecturas: {reporting.total_con_dato}");
                    if (reporting.ultimo_reporte != null)
                        sb.AppendLine($"  Último dato recibido: {((DateTime)reporting.ultimo_reporte):dd/MMM HH:mm}");
                }

                // Red stations (not transmitting)
                var redStations = await db.QueryAsync<dynamic>(@"
                    SELECT e.dcp_id, e.fecha_ultima_tx
                    FROM estatus_estaciones e
                    WHERE e.color_estatus = 'ROJO'
                    ORDER BY e.fecha_ultima_tx ASC");

                var redList = redStations.ToList();
                if (redList.Any())
                {
                    sb.AppendLine($"\n**Estaciones en ROJO ({redList.Count}):**");
                    foreach (var r in redList)
                    {
                        var lastTx = r.fecha_ultima_tx != null ? $"última tx: {((DateTime)r.fecha_ultima_tx):dd/MMM HH:mm}" : "sin dato";
                        sb.AppendLine($"  🔴 {r.dcp_id} — {lastTx}");
                    }
                }

                return sb.Length > 0 ? sb.ToString().TrimEnd() : "";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting telemetric network status for Centinela");
                return "";
            }
        }

        private async Task<string> GetCurrentVariablesSummaryAsync()
        {
            try
            {
                using var db = new NpgsqlConnection(_pgConn);

                // Summary of key variables (filtering out GOES decode errors: values < -999 or > 9999)
                var variables = await db.QueryAsync<dynamic>(@"
                    SELECT variable,
                           COUNT(*) as estaciones,
                           ROUND(AVG(valor)::numeric, 2) as promedio,
                           ROUND(MIN(valor)::numeric, 2) as minimo,
                           ROUND(MAX(valor)::numeric, 2) as maximo
                    FROM ultimas_mediciones
                    WHERE variable IN (
                        'nivel_de_agua', 'precipitación', 'temperatura',
                        'presión_atmosférica', 'humedad_relativa',
                        'velocidad_del_viento', 'dirección_del_viento',
                        'velocidad_de_ráfaga', 'voltaje_de_batería',
                        'radiación_solar', 'precipitación_acumulada'
                    )
                    AND valor > -999 AND valor < 9999
                    AND ts > NOW() - INTERVAL '4 hours'
                    GROUP BY variable
                    ORDER BY estaciones DESC");

                var varList = variables.ToList();
                if (!varList.Any()) return "";

                var varNames = new Dictionary<string, (string Name, string Unit)>
                {
                    { "nivel_de_agua", ("Nivel de agua", "m") },
                    { "precipitación", ("Precipitación", "mm") },
                    { "precipitación_acumulada", ("Precip. acumulada", "mm") },
                    { "temperatura", ("Temperatura", "°C") },
                    { "presión_atmosférica", ("Presión atmosférica", "hPa") },
                    { "humedad_relativa", ("Humedad relativa", "%") },
                    { "velocidad_del_viento", ("Velocidad viento", "m/s") },
                    { "dirección_del_viento", ("Dirección viento", "°") },
                    { "velocidad_de_ráfaga", ("Ráfaga máx", "m/s") },
                    { "voltaje_de_batería", ("Voltaje batería", "V") },
                    { "radiación_solar", ("Radiación solar", "W/m²") }
                };

                var sb = new StringBuilder();
                sb.AppendLine("**Variables de la red (últimas 4h, filtrado errores):**");
                foreach (var v in varList)
                {
                    var key = (string)v.variable;
                    var (name, unit) = varNames.GetValueOrDefault(key, (key, ""));
                    sb.AppendLine($"• **{name}**: {v.estaciones} estaciones | prom: {v.promedio} {unit} | mín: {v.minimo} {unit} | máx: {v.maximo} {unit}");
                }

                // Battery voltage issues (real low voltage: 0-10V)
                var lowBattery = await db.QueryAsync<dynamic>(@"
                    SELECT dcp_id, valor, ts
                    FROM ultimas_mediciones
                    WHERE variable = 'voltaje_de_batería'
                      AND valor >= 0 AND valor < 11.0
                      AND ts > NOW() - INTERVAL '4 hours'
                    ORDER BY valor ASC
                    LIMIT 10");

                var batList = lowBattery.ToList();
                if (batList.Any())
                {
                    sb.AppendLine($"\n⚠️ **Estaciones con voltaje bajo (<11V):**");
                    foreach (var b in batList)
                        sb.AppendLine($"  🔋 {b.dcp_id}: {b.valor:F1}V ({((DateTime)b.ts):dd/MMM HH:mm})");
                }

                // Stations with error readings (negative values = GOES decode errors)
                var errorReadings = await db.QueryAsync<dynamic>(@"
                    SELECT COUNT(DISTINCT dcp_id) as estaciones_con_error
                    FROM ultimas_mediciones
                    WHERE valor <= -999
                      AND ts > NOW() - INTERVAL '4 hours'");
                var errCount = errorReadings.FirstOrDefault();
                if (errCount != null && (long)errCount.estaciones_con_error > 0)
                    sb.AppendLine($"\n⚠️ {errCount.estaciones_con_error} estaciones con errores de decodificación GOES (valores negativos)");

                // Top nivel_de_agua readings (valid values only)
                var topNivel = await db.QueryAsync<dynamic>(@"
                    SELECT dcp_id, valor, ts
                    FROM ultimas_mediciones
                    WHERE variable = 'nivel_de_agua'
                      AND valor > 0 AND valor < 2000
                      AND ts > NOW() - INTERVAL '4 hours'
                    ORDER BY valor DESC
                    LIMIT 10");

                var nivelList = topNivel.ToList();
                if (nivelList.Any())
                {
                    sb.AppendLine($"\n**Top estaciones por nivel de agua:**");
                    foreach (var n in nivelList)
                        sb.AppendLine($"  🌊 {n.dcp_id}: {n.valor:F2} m ({((DateTime)n.ts):dd/MMM HH:mm})");
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current variables summary for Centinela");
                return "";
            }
        }

        private async Task<string> GetGOESTransmissionStatusAsync()
        {
            try
            {
                using var db = new NpgsqlConnection(_pgConn);

                // Overall GOES success rate last 24h
                var stats = await db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT 
                        COUNT(*) as total,
                        SUM(CASE WHEN exito THEN 1 ELSE 0 END) as exitosos,
                        SUM(CASE WHEN NOT exito THEN 1 ELSE 0 END) as fallidos,
                        COUNT(DISTINCT dcp_id) as estaciones_activas,
                        MIN(timestamp_utc) as desde,
                        MAX(timestamp_utc) as hasta
                    FROM bitacora_goes 
                    WHERE timestamp_utc > NOW() - INTERVAL '24 hours'");

                if (stats == null || (long)stats.total == 0) return "";

                var sb = new StringBuilder();
                double tasaExito = (long)stats.total > 0 ? 100.0 * (long)stats.exitosos / (long)stats.total : 0;
                var icon = tasaExito >= 80 ? "🟢" : tasaExito >= 50 ? "🟡" : "🔴";

                sb.AppendLine($"**Resumen GOES últimas 24h:**");
                sb.AppendLine($"  {icon} Tasa de éxito: {tasaExito:F1}% ({stats.exitosos}/{stats.total} transmisiones)");
                sb.AppendLine($"  Fallidas: {stats.fallidos}");
                sb.AppendLine($"  Estaciones activas: {stats.estaciones_activas}");
                sb.AppendLine($"  Periodo: {((DateTime)stats.desde):dd/MMM HH:mm} a {((DateTime)stats.hasta):dd/MMM HH:mm}");

                // Stations with 100% failure in last 24h
                var failures = await db.QueryAsync<dynamic>(@"
                    SELECT dcp_id, 
                           COUNT(*) as intentos,
                           SUM(CASE WHEN NOT exito THEN 1 ELSE 0 END) as fallidos
                    FROM bitacora_goes 
                    WHERE timestamp_utc > NOW() - INTERVAL '24 hours'
                    GROUP BY dcp_id
                    HAVING SUM(CASE WHEN exito THEN 1 ELSE 0 END) = 0
                    ORDER BY fallidos DESC
                    LIMIT 15");

                var failList = failures.ToList();
                if (failList.Any())
                {
                    sb.AppendLine($"\n🔴 **Estaciones sin comunicación ({failList.Count} con 0% éxito):**");
                    foreach (var f in failList.Take(10))
                        sb.AppendLine($"  ❌ {f.dcp_id}: {f.fallidos} intentos fallidos");
                    if (failList.Count > 10)
                        sb.AppendLine($"  ... y {failList.Count - 10} más");
                }

                // Stations with partial failures (>20% failure rate)
                var partialFail = await db.QueryAsync<dynamic>(@"
                    SELECT dcp_id,
                           COUNT(*) as total,
                           SUM(CASE WHEN exito THEN 1 ELSE 0 END) as exitosos,
                           SUM(CASE WHEN NOT exito THEN 1 ELSE 0 END) as fallidos
                    FROM bitacora_goes 
                    WHERE timestamp_utc > NOW() - INTERVAL '24 hours'
                    GROUP BY dcp_id
                    HAVING SUM(CASE WHEN exito THEN 1 ELSE 0 END) > 0
                       AND SUM(CASE WHEN NOT exito THEN 1 ELSE 0 END)::float / COUNT(*) > 0.2
                    ORDER BY SUM(CASE WHEN NOT exito THEN 1 ELSE 0 END) DESC
                    LIMIT 10");

                var partList = partialFail.ToList();
                if (partList.Any())
                {
                    sb.AppendLine($"\n🟡 **Estaciones con fallas parciales (>20% error):**");
                    foreach (var p in partList)
                    {
                        var rate = 100.0 * (long)p.fallidos / (long)p.total;
                        sb.AppendLine($"  ⚠️ {p.dcp_id}: {rate:F0}% error ({p.exitosos}/{p.total} exitosas)");
                    }
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting GOES transmission status for Centinela");
                return "";
            }
        }

        private async Task<string> GetActiveAlertsAsync()
        {
            try
            {
                using var db = new SqlConnection(_sqlConn);
                var alerts = await db.QueryAsync<dynamic>(@"
                    SELECT TOP 10 
                        a.Id, a.UmbralName, a.StationName, a.Variable, 
                        a.MeasuredValue, a.ThresholdValue, a.Operator,
                        a.TriggeredAt
                    FROM AlertRecord a
                    WHERE a.TriggeredAt > DATEADD(HOUR, -24, GETUTCDATE())
                    ORDER BY a.TriggeredAt DESC");

                var list = alerts.ToList();
                if (!list.Any()) return "";

                var sb = new StringBuilder();
                foreach (var a in list)
                {
                    var time = ((DateTime)a.TriggeredAt).ToLocalTime().ToString("dd/MM HH:mm");
                    sb.AppendLine($"• [{time}] **{a.StationName}** — {a.Variable}: {a.MeasuredValue:F2} {a.Operator} {a.ThresholdValue:F2} ({a.UmbralName})");
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting alerts for Centinela");
                return "";
            }
        }

        /// <summary>
        /// Search for stations by name (fuzzy LIKE match) using the NV_Estacion view.
        /// Returns station metadata including cuenca, subcuenca, coordinates, and type.
        /// </summary>
        private async Task<string> SearchStationByNameAsync(string searchTerm)
        {
            try
            {
                using var sqlDb = new SqlConnection(_sqlConn);

                // Search by name (fuzzy) using NV_Estacion pre-joined view
                var stations = await sqlDb.QueryAsync<dynamic>(@"
                    SELECT TOP 10
                        e.Id, e.Nombre, e.IdAsignado, e.Latitud, e.Longitud,
                        e.NombreCuenca, e.NombreSubcuenca, e.NombreOrganizacion,
                        e.Activo, e.Visible, e.GOES, e.RADIO, e.GPRS, e.EsPresa, e.Etiqueta,
                        e.NombreEntidadFederativa, e.NombreMunicipio,
                        dg.IdSatelital
                    FROM NV_Estacion e
                    LEFT JOIN DatosGOES dg ON dg.IdEstacion = e.Id
                    WHERE e.Nombre LIKE @Search
                       OR e.IdAsignado LIKE @Search
                       OR e.Etiqueta LIKE @Search
                    ORDER BY e.Activo DESC, e.Nombre",
                    new { Search = $"%{searchTerm}%" });

                var stationList = stations.ToList();
                if (!stationList.Any()) return "";

                var sb = new StringBuilder();
                sb.AppendLine($"**Estaciones encontradas ({stationList.Count}):**\n");

                foreach (var s in stationList)
                {
                    var tipo = new List<string>();
                    if (s.GOES != null && (bool)s.GOES) tipo.Add("GOES");
                    if (s.GPRS != null && (bool)s.GPRS) tipo.Add("GPRS");
                    if (s.RADIO != null && (bool)s.RADIO) tipo.Add("RADIO");
                    if (s.EsPresa != null && (bool)s.EsPresa) tipo.Add("PRESA");
                    var tipoStr = tipo.Any() ? string.Join("/", tipo) : "N/D";
                    var estadoStr = (s.Activo != null && (bool)s.Activo) ? "Activa" : "Inactiva";

                    sb.AppendLine($"**{s.Nombre}** ({s.IdAsignado})");
                    sb.AppendLine($"  Estado: {estadoStr} | Tipo: {tipoStr}");
                    sb.AppendLine($"  Cuenca: {s.NombreCuenca ?? "N/D"} | Subcuenca: {s.NombreSubcuenca ?? "N/D"}");
                    if (s.Latitud != null && s.Longitud != null)
                        sb.AppendLine($"  Ubicación: {s.Latitud:F4}°N, {s.Longitud:F4}°W");
                    if (s.NombreEntidadFederativa != null)
                        sb.AppendLine($"  {s.NombreMunicipio ?? ""}, {s.NombreEntidadFederativa}");
                    if (s.IdSatelital != null)
                        sb.AppendLine($"  DCP GOES: {s.IdSatelital}");

                    // Get sensors for this station
                    var sensors = await GetStationSensorsAsync(sqlDb, (Guid)s.Id);
                    if (!string.IsNullOrEmpty(sensors))
                        sb.AppendLine($"  Sensores:\n{sensors}");

                    // Get current readings from PostgreSQL (using hex DCP address from DatosGOES)
                    var dcpId = (string?)s.IdSatelital ?? (string)s.IdAsignado;
                    var readings = await GetStationCurrentReadingsAsync(dcpId);
                    if (!string.IsNullOrEmpty(readings))
                        sb.AppendLine($"  Lecturas actuales:\n{readings}");

                    sb.AppendLine();
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching station by name for Centinela: {Search}", searchTerm);
                return "";
            }
        }

        /// <summary>
        /// Get sensor configuration for a station from SQL Server.
        /// </summary>
        private async Task<string> GetStationSensorsAsync(SqlConnection sqlDb, Guid stationId)
        {
            try
            {
                var sensors = await sqlDb.QueryAsync<dynamic>(@"
                    SELECT s.NumeroSensor, ts.Nombre as TipoSensor, um.Nombre as Unidad,
                           s.Activo, s.Funciona, s.ValorMinimo, s.ValorMaximo
                    FROM Sensor s
                    INNER JOIN TipoSensor ts ON ts.Id = s.IdTipoSensor
                    INNER JOIN UnidadMedida um ON um.Id = s.IdUnidadMedida
                    WHERE s.IdEstacion = @StationId AND s.Activo = 1
                    ORDER BY s.NumeroSensor",
                    new { StationId = stationId });

                var sensorList = sensors.ToList();
                if (!sensorList.Any()) return "";

                var sb = new StringBuilder();
                foreach (var sensor in sensorList)
                {
                    var status = (sensor.Funciona != null && (bool)sensor.Funciona) ? "✅" : "⚠️";
                    sb.Append($"    {status} {sensor.TipoSensor} ({sensor.Unidad})");
                    if (sensor.ValorMinimo != null && sensor.ValorMaximo != null)
                        sb.Append($" [rango: {sensor.ValorMinimo:F1} - {sensor.ValorMaximo:F1}]");
                    sb.AppendLine();
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sensors for station {StationId}", stationId);
                return "";
            }
        }

        /// <summary>
        /// Get current readings for a station from PostgreSQL ultimas_mediciones.
        /// Matches by dcp_id (hex GOES address from DatosGOES table).
        /// </summary>
        private async Task<string> GetStationCurrentReadingsAsync(string dcpId)
        {
            try
            {
                using var pgDb = new NpgsqlConnection(_pgConn);

                var readings = await pgDb.QueryAsync<dynamic>(@"
                    SELECT variable, valor, ts, dcp_id
                    FROM ultimas_mediciones
                    WHERE dcp_id = @Id
                      AND ts > NOW() - INTERVAL '6 hours'
                      AND valor > -999 AND valor < 9999
                    ORDER BY variable",
                    new { Id = dcpId });

                var readingList = readings.ToList();
                if (!readingList.Any()) return "";

                var varNames = new Dictionary<string, (string Name, string Unit)>
                {
                    { "nivel_de_agua", ("Nivel de agua", "m") },
                    { "precipitación", ("Precipitación", "mm") },
                    { "precipitación_acumulada", ("Precip. acumulada", "mm") },
                    { "temperatura", ("Temperatura", "°C") },
                    { "temperatura_interna", ("Temp. interna", "°C") },
                    { "presión_atmosférica", ("Presión", "hPa") },
                    { "humedad_relativa", ("Humedad", "%") },
                    { "humedad_del_aire", ("Humedad aire", "%") },
                    { "velocidad_del_viento", ("Viento", "m/s") },
                    { "dirección_del_viento", ("Dir. viento", "°") },
                    { "velocidad_de_ráfaga", ("Ráfaga", "m/s") },
                    { "voltaje_de_batería", ("Batería", "V") },
                    { "radiación_solar", ("Radiación", "W/m²") },
                    { "señal_de_ruido", ("Señal ruido", "dB") },
                    { "punto_de_rocío", ("Punto rocío", "°C") }
                };

                var sb = new StringBuilder();
                foreach (var r in readingList)
                {
                    var key = (string)r.variable;
                    var (name, unit) = varNames.GetValueOrDefault(key, (key, ""));
                    var ts = ((DateTime)r.ts).ToLocalTime().ToString("HH:mm");
                    sb.AppendLine($"    📊 {name}: {r.valor:F2} {unit} ({ts})");
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current readings for station {Id}", dcpId);
                return "";
            }
        }

        #endregion
    }
}
