using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NLog;

namespace StoreManagement.Api.Services
{
    public class GeminiVisionService : IGeminiVisionService
    {
        private static readonly Logger Logger = LogManager.GetLogger("GeminiVisionService");
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public GeminiVisionService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["Gemini:ApiKey"] ?? string.Empty;
        }

        public async Task<string?> ExtractBillDataAsync(IFormFile imageFile)
        {
            if (string.IsNullOrEmpty(_apiKey) || _apiKey == "YOUR_GEMINI_API_KEY")
            {
                Logger.Error("Gemini API key is not configured.");
                throw new Exception("Gemini API key is missing. Please configure it in appsettings.json.");
            }

            try
            {
                using var ms = new MemoryStream();
                await imageFile.CopyToAsync(ms);
                var imageBytes = ms.ToArray();
                var base64Image = Convert.ToBase64String(imageBytes);
                var mimeType = imageFile.ContentType;
                if (string.IsNullOrEmpty(mimeType) || !mimeType.StartsWith("image/"))
                {
                    mimeType = "image/jpeg";
                }

                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new { text = "Extract all tabular data from this wholesale bill/receipt image. I need the list of products purchased. Return a JSON array where each object has the requested fields. If the bill only lists serial numbers (e.g., S NO. 1, 2, 3...) and no product names, generate placeholder names like 'Item 1', 'Item 2', etc." },
                                new
                                {
                                    inlineData = new
                                    {
                                        mimeType = mimeType,
                                        data = base64Image
                                    }
                                }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.1,
                        responseMimeType = "application/json",
                        responseSchema = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    ProductName = new { type = "string", description = "The name of the product. If no name is explicitly written, use 'Item' followed by its Serial Number or row index (e.g., 'Item 1')." },
                                    Quantity = new { type = "integer", description = "Quantity of the product purchased" },
                                    CostPrice = new { type = "number", description = "The cost price or unit rate per item" },
                                    TotalAmount = new { type = "number", description = "Total amount for this product item" }
                                },
                                required = new[] { "ProductName", "Quantity", "CostPrice", "TotalAmount" }
                            }
                        }
                    }
                };

                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                var requestUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.6-flash:generateContent?key={_apiKey}";

                var response = await _httpClient.PostAsync(requestUrl, content);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorStr = await response.Content.ReadAsStringAsync();
                    Logger.Error($"Gemini API failed with status {response.StatusCode}: {errorStr}");
                    return null;
                }

                var responseString = await response.Content.ReadAsStringAsync();
                using var jsonDoc = JsonDocument.Parse(responseString);
                var root = jsonDoc.RootElement;

                // Navigate through Gemini's response structure to get the text
                if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var firstCandidate = candidates[0];
                    if (firstCandidate.TryGetProperty("content", out var resContent) && 
                        resContent.TryGetProperty("parts", out var parts) && 
                        parts.GetArrayLength() > 0)
                    {
                        var textPart = parts[0];
                        if (textPart.TryGetProperty("text", out var textNode))
                        {
                            return textNode.GetString();
                        }
                    }
                }

                Logger.Warn("Could not parse text from Gemini response structure.");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Exception while calling Gemini API");
                return null;
            }
        }
    }
}
