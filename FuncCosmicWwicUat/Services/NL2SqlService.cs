using FuncCosmicWwicUat.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace FuncCosmicWwicUat.Services;

public class NL2SqlService
{
    private readonly ILogger<NL2SqlService> _logger;
    private readonly AzureOpenAIResponsesClient _responsesClient;

    public NL2SqlService(AzureOpenAIResponsesClient responsesClient, ILogger<NL2SqlService> logger)
    {
        _logger = logger;
        _responsesClient = responsesClient;
        _logger.LogInformation("NL2SqlService initialized (using Responses API)");
    }

    // ─── Build schema context string from MCP table list ─────────────────────
    public string BuildSchemaContext(List<McpSchemaTable> tables)
    {
        if (tables.Count == 0)
            return "Available tables: dbo.Cases (default)";

        var lines = tables.Select(t => $"- [{t.Schema}].[{t.Name}] ({t.Type})");
        return "Available tables in the database:\n" + string.Join("\n", lines);
    }

    // ─── Generate SQL from natural language using schema context ──────────────
    public async Task<string> GenerateSqlAsync(string userMessage, string schemaContext)
    {
        var systemPrompt = $"""
            You are an expert Microsoft SQL Server query generator.

            {schemaContext}

            Rules:
            1. Return ONLY the SQL query. No explanation. No markdown. No backticks.
            2. Only generate SELECT queries.
            3. Never use INSERT, UPDATE, DELETE, DROP, ALTER, TRUNCATE, MERGE, CREATE, EXEC, EXECUTE.
            4. Use SQL Server syntax (TOP instead of LIMIT).
            5. Default to TOP 10 unless the user specifies a different number.
            6. Use fully qualified table names e.g. [dbo].[Cases].
            7. If the user asks for "all columns", use SELECT TOP 10 *.
            8. End the query with a semicolon.
            """;

        var raw = await _responsesClient.CreateResponseTextAsync(
            systemPrompt, userMessage, temperature: 0f, maxCompletionTokens: 400);

        _logger.LogInformation("Generated SQL (raw): {Sql}", raw);

        return CleanSql(raw);
    }

    // ─── Format human-readable reply from query results ──────────────────────
    public async Task<string> FormatResultsAsync(
        string userMessage,
        string generatedSql,
        int rowCount,
        object? rows)
    {
        var systemPrompt = """
            You are a helpful data assistant.
            The user asked a question, a SQL query was run, and results were returned.
            Summarize the results in plain English, concisely.
            If there are 0 rows, say no data was found.
            Do not mention SQL or technical details unless asked.
            """;

        var userContent = $"""
            User question: {userMessage}
            SQL executed: {generatedSql}
            Row count: {rowCount}
            Results (JSON): {System.Text.Json.JsonSerializer.Serialize(rows)}
            """;

        return await _responsesClient.CreateResponseTextAsync(
            systemPrompt, userContent, temperature: 0.3f, maxCompletionTokens: 500);
    }

    // ─── Validate SQL is safe (SELECT only) ───────────────────────────────────
    public (bool IsValid, string Error) ValidateSql(string query)
    {
        if (string.IsNullOrWhiteSpace(query) ||
            !query.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            return (false, "Only SELECT queries are allowed.");

        var blocked = new[] { "insert", "update", "delete", "drop", "alter",
                               "truncate", "merge", "exec", "execute", "create" };

        var q = query.ToLowerInvariant();
        foreach (var word in blocked)
        {
            if (Regex.IsMatch(q, $@"\b{word}\b"))
                return (false, $"Blocked keyword detected: {word.ToUpper()}");
        }

        var qWithoutTrailingSemicolon = q.TrimEnd(';');
        if (qWithoutTrailingSemicolon.Contains(';'))
            return (false, "Multiple SQL statements are not allowed.");

        return (true, string.Empty);
    }

    // ─── Strip markdown code fences if LLM wraps output ─────────────────────
    private static string CleanSql(string sql)
    {
        sql = Regex.Replace(sql, @"^```sql\s*", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        sql = Regex.Replace(sql, @"^```\s*",    "", RegexOptions.Multiline);
        sql = Regex.Replace(sql, @"\s*```$",    "", RegexOptions.Multiline);
        sql = sql.Trim().TrimEnd(';') + ";";
        return sql;
    }
}
