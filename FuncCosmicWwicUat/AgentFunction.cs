using System.Net;
using System.Text.Json;
using FuncCosmicWwicUat.Models;
using FuncCosmicWwicUat.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FuncCosmicWwicUat;

public class AgentFunction
{
    private readonly IntentService _intentService;
    private readonly NL2SqlService _nl2SqlService;
    private readonly ProcessMiningService _processMiningService;
    private readonly McpClientService _mcpClient;
    private readonly ILogger<AgentFunction> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public AgentFunction(
        IntentService intentService,
        NL2SqlService nl2SqlService,
        ProcessMiningService processMiningService,
        McpClientService mcpClient,
        ILogger<AgentFunction> logger)
    {
        _intentService  = intentService;
        _nl2SqlService  = nl2SqlService;
        _processMiningService = processMiningService;
        _mcpClient      = mcpClient;
        _logger         = logger;
    }

    // ─── Diagnostic/Status Endpoint ────────────────────────────────────────────
    // GET /api/status
    [Function("Status")]
    public async Task<HttpResponseData> StatusAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "status")] HttpRequestData req)
    {
        try
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                status = "ok",
                function = "func-cosmic-wwic-uat-westus",
                timestamp = DateTime.UtcNow
            }, _jsonOpts));
            response.Headers.Add("Content-Type", "application/json");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Status endpoint error");
            return await ErrorAsync(req, $"Status check failed: {ex.Message}");
        }
    }

    // ─── Main Agent Endpoint (Teams / Copilot connects here) ─────────────────
    // POST /api/agent
    // Body: { "message": "Show me the top 5 open cases", "sessionId": "optional" }
    [Function("Agent")]
    public async Task<HttpResponseData> AgentAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agent")] HttpRequestData req)
    {
        _logger.LogInformation("Agent endpoint triggered.");

        AgentRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<AgentRequest>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return await BadRequestAsync(req, "Invalid JSON body.");
        }

        if (request == null || string.IsNullOrWhiteSpace(request.Message))
            return await BadRequestAsync(req, "Missing 'message' in request body.");

        _logger.LogInformation("User message: {Message}", request.Message);

        try
        {
            // ── Step 1: Detect intent ──────────────────────────────────────
            string intent;
            string reasoning;
            try
            {
                var intentResult = await _intentService.DetectIntentAsync(request.Message);
                intent = intentResult.Intent;
                reasoning = intentResult.Reasoning;
                _logger.LogInformation("Intent: {Intent} — {Reasoning}", intent, reasoning);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Intent detection failed");
                return await ErrorAsync(req, $"Intent detection failed: {ex.Message}");
            }

            // ── Step 2: Route by intent ────────────────────────────────────
            if (intent == "NL2SQL")
            {
                return await HandleNL2SqlAsync(req, request.Message, intent);
            }
            else if (intent == "PROCESS_MINING")
            {
                return await HandleProcessMiningAsync(req, request.Message, intent);
            }
            else
            {
                try
                {
                    var reply = await _intentService.GeneralChatAsync(request.Message);
                    return await OkAsync(req, new AgentResponse
                    {
                        Status  = "success",
                        Intent  = intent,
                        Reply   = reply
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "General chat failed");
                    return await ErrorAsync(req, $"General chat failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected agent error: {Message}", ex.Message);
            return await ErrorAsync(req, $"Unexpected error: {ex.Message}");
        }
    }

    // ─── Health Check ─────────────────────────────────────────────────────────
    // GET /api/health
    [Function("Health")]
    public async Task<HttpResponseData> HealthAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        try
        {
            string mcpHealth;
            try
            {
                mcpHealth = await _mcpClient.HealthCheckAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("MCP health check failed: {Error}", ex.Message);
                mcpHealth = $"unavailable: {ex.Message}";
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                status = "healthy",
                function = "func-cosmic-wwic-uat-westus",
                mcpServer = mcpHealth,
                timestamp = DateTime.UtcNow
            }, _jsonOpts));
            response.Headers.Add("Content-Type", "application/json");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return await ErrorAsync(req, $"Health check failed: {ex.Message}");
        }
    }

    // ─── NL2SQL Flow ──────────────────────────────────────────────────────────
    private async Task<HttpResponseData> HandleNL2SqlAsync(
        HttpRequestData req, string userMessage, string intent)
    {
        try
        {
            // Step A: Get schema from MCP Server
            List<McpSchemaTable> tables;
            try
            {
                tables = await _mcpClient.GetTablesAsync();
                _logger.LogInformation("Schema fetched: {Count} tables", tables.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Schema fetch failed, using default: {Error}", ex.Message);
                tables = new List<McpSchemaTable>();
            }

            // Step B: Build schema context + generate SQL
            string generatedSql;
            try
            {
                var schemaContext = _nl2SqlService.BuildSchemaContext(tables);
                generatedSql = await _nl2SqlService.GenerateSqlAsync(userMessage, schemaContext);
                _logger.LogInformation("Generated SQL: {Sql}", generatedSql);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SQL generation failed");
                return await ErrorAsync(req, $"SQL generation failed: {ex.Message}");
            }

            // Step C: Validate SQL
            var (isValid, validationError) = _nl2SqlService.ValidateSql(generatedSql);
            if (!isValid)
            {
                return await OkAsync(req, new AgentResponse
                {
                    Status       = "failed",
                    Intent       = intent,
                    Reply        = $"Could not generate a safe query: {validationError}",
                    GeneratedSql = generatedSql
                });
            }

            // Step D: Execute SQL via MCP Server
            McpExecuteResponse mcpResult;
            try
            {
                mcpResult = await _mcpClient.ExecuteSqlAsync(generatedSql);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SQL execution failed");
                return await ErrorAsync(req, $"SQL execution failed: {ex.Message}");
            }

            if (mcpResult.Status != "success")
            {
                return await OkAsync(req, new AgentResponse
                {
                    Status       = "failed",
                    Intent       = intent,
                    Reply        = $"Query execution failed: {mcpResult.Message}",
                    GeneratedSql = generatedSql
                });
            }

            // Step E: Format results into natural language reply
            string reply;
            try
            {
                reply = await _nl2SqlService.FormatResultsAsync(
                    userMessage, generatedSql, mcpResult.RowCount, mcpResult.Rows);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Result formatting failed");
                return await ErrorAsync(req, $"Result formatting failed: {ex.Message}");
            }

            return await OkAsync(req, new AgentResponse
            {
                Status       = "success",
                Intent       = intent,
                Reply        = reply,
                GeneratedSql = generatedSql,
                RowCount     = mcpResult.RowCount,
                Rows         = mcpResult.Rows
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in NL2SQL flow");
            return await ErrorAsync(req, $"Unexpected error in NL2SQL flow: {ex.Message}");
        }
    }

    // ─── Process Mining Flow ──────────────────────────────────────────────────
    private async Task<HttpResponseData> HandleProcessMiningAsync(
        HttpRequestData req, string userMessage, string intent)
    {
        try
        {
            // Step A: Search for process mining documents in blob storage
            List<BlobDocument> documents;
            try
            {
                documents = await _processMiningService.SearchDocumentsAsync(userMessage);
                _logger.LogInformation("Found {Count} process mining documents", documents.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search process mining documents");
                return await ErrorAsync(req, $"Failed to search process mining documents: {ex.Message}");
            }

            if (documents.Count == 0)
            {
                return await OkAsync(req, new AgentResponse
                {
                    Status  = "success",
                    Intent  = intent,
                    Reply   = "No process mining documents found for your query. Please upload process mining documents to the blob storage."
                });
            }

            // Step B: Extract and analyze process details from documents
            string analysisResult;
            try
            {
                analysisResult = await _processMiningService.ExtractProcessDetailsAsync(documents, userMessage);
                _logger.LogInformation("Process mining analysis completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract process mining details");
                return await ErrorAsync(req, $"Failed to analyze process mining documents: {ex.Message}");
            }

            // Step C: Format the response
            string formattedReply;
            try
            {
                formattedReply = await _processMiningService.FormatProcessMiningReplyAsync(analysisResult, userMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to format reply, using raw analysis");
                formattedReply = analysisResult;
            }

            return await OkAsync(req, new AgentResponse
            {
                Status  = "success",
                Intent  = intent,
                Reply   = formattedReply,
                Rows    = documents.Select(d => new { d.Name, d.Size, d.Modified }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in Process Mining flow");
            return await ErrorAsync(req, $"Unexpected error in Process Mining flow: {ex.Message}");
        }
    }

    // ─── Response Helpers ─────────────────────────────────────────────────────
    private static async Task<HttpResponseData> OkAsync(HttpRequestData req, AgentResponse body)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, _jsonOpts));
        return response;
    }

    private static async Task<HttpResponseData> BadRequestAsync(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(
            new { status = "error", message }, _jsonOpts));
        return response;
    }

    private static async Task<HttpResponseData> ErrorAsync(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.InternalServerError);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(
            new { status = "error", message }, _jsonOpts));
        return response;
    }
}