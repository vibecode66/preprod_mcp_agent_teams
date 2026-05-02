using FuncCosmicWwicUat.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FuncCosmicWwicUat.Services;

public class IntentService
{
    private readonly ILogger<IntentService> _logger;
    private readonly AzureOpenAIResponsesClient _responsesClient;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IntentService(AzureOpenAIResponsesClient responsesClient, ILogger<IntentService> logger)
    {
        _logger = logger;
        _responsesClient = responsesClient;
        _logger.LogInformation("IntentService initialized (using Responses API)");
    }

    // ─── Classify the user's intent ───────────────────────────────────────────
    public async Task<IntentResult> DetectIntentAsync(string userMessage)
    {
        const string systemPrompt = """
            You are an intent classifier for a data assistant.

            Classify the user message into EXACTLY one of these intents:
            - PROCESS_MINING : user wants process mining analysis, workflow, process flow, process discovery, mining insights
            - NL2SQL : user wants to query, search, list, count, find, show data from a database
            - GENERAL : everything else (greetings, help, explanations, non-data questions)

            Respond with ONLY valid JSON in this exact format:
            {"intent": "PROCESS_MINING", "reasoning": "brief reason"}
            or
            {"intent": "NL2SQL", "reasoning": "brief reason"}
            or
            {"intent": "GENERAL", "reasoning": "brief reason"}

            Do not add any text outside the JSON.
            """;

        var raw = await _responsesClient.CreateResponseTextAsync(
            systemPrompt, userMessage, temperature: 0f, maxCompletionTokens: 100);

        _logger.LogInformation("Intent raw response: {Raw}", raw);

        try
        {
            return JsonSerializer.Deserialize<IntentResult>(raw, _jsonOpts)
                   ?? new IntentResult { Intent = "GENERAL", Reasoning = "Parse failed" };
        }
        catch
        {
            return new IntentResult { Intent = "GENERAL", Reasoning = "Could not parse intent" };
        }
    }

    // ─── General chat (non-SQL) ───────────────────────────────────────────────
    public async Task<string> GeneralChatAsync(string userMessage)
    {
        const string systemPrompt = """
            You are a helpful assistant for the WWIC application.
            Answer concisely and professionally.
            If the user asks about data, records, or queries,
            tell them to ask in natural language like "Show me the top 10 cases".
            """;

        return await _responsesClient.CreateResponseTextAsync(
            systemPrompt, userMessage, temperature: 0.7f, maxCompletionTokens: 600);
    }
}
