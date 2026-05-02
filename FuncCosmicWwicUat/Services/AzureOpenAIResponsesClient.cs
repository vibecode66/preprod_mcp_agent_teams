using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace FuncCosmicWwicUat.Services;

public class AzureOpenAIResponsesClient
{
    private readonly HttpClient _http;
    private readonly ManagedIdentityCredential _credential;
    private readonly ILogger<AzureOpenAIResponsesClient> _logger;

    private readonly Uri _endpoint;
    private readonly string _deployment;
    private readonly string _apiVersion;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AzureOpenAIResponsesClient(
        HttpClient http,
        ManagedIdentityCredential credential,
        ILogger<AzureOpenAIResponsesClient> logger)
    {
        _http = http;
        _credential = credential;
        _logger = logger;

        _endpoint = new Uri(
            Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
            ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is required."));

        // IMPORTANT: This must be the DEPLOYMENT name (e.g., "gpt-5.2")
        _deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL")
            ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL (deployment name) is required.");

        // Responses API enabled for 2025-03-01-preview and later
        _apiVersion = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION")
            ?? "2025-03-01-preview";
    }

    private async Task SetAuthHeaderAsync()
    {
        var scope = "https://cognitiveservices.azure.com/.default";
        var token = await _credential.GetTokenAsync(new TokenRequestContext(new[] { scope }));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
    }

    public async Task<string> CreateResponseTextAsync(
        string systemPrompt,
        string userMessage,
        float temperature,
        int maxCompletionTokens)
    {
        await SetAuthHeaderAsync();

        var url = new Uri(_endpoint, $"/openai/responses?api-version={_apiVersion}");

        // NOTE:
        // - Input content type must be "input_text"
        // - Output content type to parse is typically "output_text"
        // - Token limit parameter is "max_output_tokens" (not max_tokens / max_completion_tokens)
        var payload = new
        {
            model = _deployment,
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = new object[]
                    {
                        new { type = "input_text", text = systemPrompt }
                    }
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = userMessage }
                    }
                }
            },
            temperature = temperature,
            max_output_tokens = maxCompletionTokens
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.PostAsync(url, content);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Azure OpenAI Responses error ({Status}): {Body}", (int)resp.StatusCode, body);
            throw new InvalidOperationException(body);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("output", out var outputArr) &&
                outputArr.ValueKind == JsonValueKind.Array &&
                outputArr.GetArrayLength() > 0)
            {
                var first = outputArr[0];

                if (first.TryGetProperty("content", out var contentArr) &&
                    contentArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in contentArr.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var typeEl) &&
                            typeEl.GetString() is string t &&
                            t.Equals("output_text", StringComparison.OrdinalIgnoreCase))
                        {
                            if (item.TryGetProperty("text", out var textEl))
                                return textEl.GetString()?.Trim() ?? string.Empty;
                        }
                    }
                }
            }

            _logger.LogWarning("Could not parse response text from Responses API. Returning raw body.");
            return body;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Responses API JSON. Returning raw body.");
            return body;
        }
    }
}