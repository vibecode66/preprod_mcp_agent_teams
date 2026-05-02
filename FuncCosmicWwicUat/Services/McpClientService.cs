using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using FuncCosmicWwicUat.Models;
using Microsoft.Extensions.Logging;

namespace FuncCosmicWwicUat.Services;

public class McpClientService
{
    private readonly HttpClient _http;
    private readonly ManagedIdentityCredential _credential;
    private readonly ILogger<McpClientService> _logger;

    private readonly Uri _baseUri;
    private readonly string _audience;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public McpClientService(
        HttpClient http,
        ManagedIdentityCredential credential,
        ILogger<McpClientService> logger)
    {
        _http = http;
        _credential = credential;
        _logger = logger;

        // Base URL for the MCP server (REQUIRED)
        var baseUrl = Environment.GetEnvironmentVariable("MCP_SERVER_BASE_URL")
                      ?? "https://app-cosmic-wwic-uat-eastus.azurewebsites.net";

        _baseUri = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.BaseAddress = _baseUri;

        // Audience for Easy Auth token acquisition (usually same as base URL OR app client id)
        _audience = Environment.GetEnvironmentVariable("MCP_SERVER_AUDIENCE")
                    ?? baseUrl;

        _logger.LogInformation("McpClientService BaseAddress={Base} Audience={Audience}", _http.BaseAddress, _audience);
    }

    private async Task SetAuthHeaderAsync()
    {
        try
        {
            // Easy Auth typically expects a token for: <audience>/.default
            var scope = _audience.TrimEnd('/') + "/.default";
            var token = await _credential.GetTokenAsync(new TokenRequestContext(new[] { scope }));

            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not acquire MI token for MCP Server: {Error}", ex.Message);
            // Continue without token (may work if MCP is anonymous)
        }
    }

    private async Task<string> SendAndReadAsync(HttpRequestMessage req)
    {
        await SetAuthHeaderAsync();

        using var resp = await _http.SendAsync(req);
        var contentType = resp.Content.Headers.ContentType?.MediaType ?? "(none)";
        var body = await resp.Content.ReadAsStringAsync();

        _logger.LogInformation("MCP {Method} {Url} -> {Status} content-type={ContentType}",
            req.Method, req.RequestUri, (int)resp.StatusCode, contentType);

        if (!resp.IsSuccessStatusCode)
        {
            var preview = body.Length > 800 ? body[..800] : body;
            throw new InvalidOperationException(
                $"MCP call failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Content-Type={contentType}. Body preview: {preview}");
        }

        if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            var preview = body.Length > 800 ? body[..800] : body;
            throw new InvalidOperationException(
                $"MCP returned non-JSON response. Content-Type={contentType}. Body preview: {preview}");
        }

        return body;
    }

    public async Task<List<McpSchemaTable>> GetTablesAsync()
    {
        var json = await SendAndReadAsync(new HttpRequestMessage(HttpMethod.Get, "api/sqlquery/tables"));
        return JsonSerializer.Deserialize<List<McpSchemaTable>>(json, _jsonOpts) ?? new List<McpSchemaTable>();
    }

    public async Task<McpExecuteResponse> ExecuteSqlAsync(string sqlQuery)
    {
        var payload = JsonSerializer.Serialize(new McpExecuteRequest { SqlQuery = sqlQuery });
        var req = new HttpRequestMessage(HttpMethod.Post, "api/sqlquery/execute")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var json = await SendAndReadAsync(req);

        return JsonSerializer.Deserialize<McpExecuteResponse>(json, _jsonOpts)
               ?? new McpExecuteResponse { Status = "error", Message = "Empty response from MCP Server" };
    }

    public async Task<string> HealthCheckAsync()
    {
        var json = await SendAndReadAsync(new HttpRequestMessage(HttpMethod.Get, "api/sqlquery/health"));
        return json;
    }

    // ─── Blob Storage Methods ─────────────────────────────────────────────────
    public async Task<List<BlobDocument>> ListBlobsAsync(string containerName)
    {
        var json = await SendAndReadAsync(
            new HttpRequestMessage(HttpMethod.Post, "api/blobstorage/list")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { containerName }),
                    Encoding.UTF8,
                    "application/json")
            });

        try
        {
            var response = JsonSerializer.Deserialize<BlobListResponse>(json, _jsonOpts);
            return response?.Blobs ?? new List<BlobDocument>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse blob list response");
            return new List<BlobDocument>();
        }
    }

    public async Task<string> DownloadBlobAsStringAsync(string containerName, string blobName)
    {
        var payload = JsonSerializer.Serialize(new
        {
            containerName,
            blobName
        });

        var req = new HttpRequestMessage(HttpMethod.Post, "api/blobstorage/download")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var json = await SendAndReadAsync(req);
        
        try
        {
            var response = JsonSerializer.Deserialize<dynamic>(json, _jsonOpts);
            return response?.ToString() ?? json;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse blob download response");
            return json;
        }
    }

    public async Task<string> BlobHealthCheckAsync()
    {
        var json = await SendAndReadAsync(
            new HttpRequestMessage(HttpMethod.Get, "api/blobstorage/health"));
        return json;
    }
}