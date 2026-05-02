using FuncCosmicWwicUat.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FuncCosmicWwicUat.Services;

public class ProcessMiningService
{
    private readonly McpClientService _mcpClient;
    private readonly AzureOpenAIResponsesClient _llmClient;
    private readonly ILogger<ProcessMiningService> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ProcessMiningService(
        McpClientService mcpClient,
        AzureOpenAIResponsesClient llmClient,
        ILogger<ProcessMiningService> logger)
    {
        _mcpClient = mcpClient;
        _llmClient = llmClient;
        _logger = logger;
    }

    // ─── Search for process mining documents in blob storage ──────────────────
    public async Task<List<BlobDocument>> SearchDocumentsAsync(
        string userQuery, string containerName = "copilotmcpagentdocuments")
    {
        try
        {
            var blobs = await _mcpClient.ListBlobsAsync(containerName);
            _logger.LogInformation("Found {Count} process mining documents", blobs.Count);

            // Filter blobs based on user query keywords
            var keywords = ExtractKeywords(userQuery);
            var matchingBlobs = FilterBlobsByKeywords(blobs, keywords);

            _logger.LogInformation("Filtered to {Count} matching documents for keywords: {Keywords}",
                matchingBlobs.Count, string.Join(", ", keywords));

            return matchingBlobs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search for process mining documents");
            throw;
        }
    }

    // ─── Extract and analyze process mining details from documents ────────────
    public async Task<string> ExtractProcessDetailsAsync(
        List<BlobDocument> documents, string userQuery, string containerName = "copilotmcpagentdocuments")
    {
        if (documents.Count == 0)
            return "No process mining documents found for your query.";

        var documentContents = new List<string>();

        // Download and read document contents
        foreach (var doc in documents.Take(5)) // Limit to first 5 documents to avoid token limits
        {
            try
            {
                var content = await _mcpClient.DownloadBlobAsStringAsync(containerName, doc.Name);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    // Limit content size per document (first 2000 chars)
                    var truncated = content.Length > 2000 ? content[..2000] + "..." : content;
                    documentContents.Add($"--- Document: {doc.Name} ---\n{truncated}");
                    _logger.LogInformation("Downloaded document: {Name} ({Size} chars)", 
                        doc.Name, truncated.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download document {Name}", doc.Name);
            }
        }

        if (documentContents.Count == 0)
            return "Could not retrieve process mining documents from storage.";

        // Use LLM to extract relevant process information
        const string systemPrompt = """
            You are a process mining analyst. 
            Based on the provided documents, answer the user's question about process flows, workflows, and process mining insights.
            Focus on:
            - Process steps and their sequence
            - Decision points and branching
            - Actors and roles involved
            - Performance metrics and bottlenecks
            - Compliance requirements
            Be concise and extract only the relevant information for the user's specific question.
            """;

        var documentContext = string.Join("\n\n", documentContents);
        var userPrompt = $"User Question: {userQuery}\n\nDocuments:\n{documentContext}";

        try
        {
            var analysis = await _llmClient.CreateResponseTextAsync(
                systemPrompt, userPrompt, temperature: 0.3f, maxCompletionTokens: 1000);

            _logger.LogInformation("Process mining analysis completed");
            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze process mining documents");
            throw;
        }
    }

    // ─── Format process mining reply ───────────────────────────────────────────
    public async Task<string> FormatProcessMiningReplyAsync(string analysisResult, string userQuery)
    {
        const string systemPrompt = """
            You are a helpful assistant formatting process mining analysis results.
            Format the analysis in a clear, professional way.
            Include:
            - A brief summary of the process
            - Key findings
            - Any metrics or performance indicators mentioned
            Keep the response concise and well-structured.
            """;

        var userPrompt = $"Format this process mining analysis as a clear response to the question '{userQuery}':\n\n{analysisResult}";

        try
        {
            var formattedReply = await _llmClient.CreateResponseTextAsync(
                systemPrompt, userPrompt, temperature: 0.7f, maxCompletionTokens: 800);

            return formattedReply;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to format reply, returning raw analysis");
            return analysisResult;
        }
    }

    // ─── Helper: Extract keywords from user query ─────────────────────────────
    private List<string> ExtractKeywords(string userQuery)
    {
        var keywords = new List<string>();
        var terms = userQuery.ToLower().Split(new[] { ' ', ',', '.', '?', '!' }, 
            StringSplitOptions.RemoveEmptyEntries);

        // Filter out common stop words
        var stopWords = new[] { "the", "a", "an", "and", "or", "but", "is", "are", "was", "were", 
                               "what", "how", "when", "where", "why", "can", "could", "should", 
                               "would", "do", "does", "did", "to", "for", "of", "in", "on", "at" };

        foreach (var term in terms)
        {
            if (term.Length > 3 && !stopWords.Contains(term))
            {
                keywords.Add(term);
            }
        }

        return keywords.Take(5).ToList(); // Use top 5 keywords
    }

    // ─── Helper: Filter blobs by keywords ──────────────────────────────────────
    private List<BlobDocument> FilterBlobsByKeywords(List<BlobDocument> blobs, List<string> keywords)
    {
        if (keywords.Count == 0)
            return blobs.Take(3).ToList(); // Return first 3 if no keywords

        var scored = blobs.Select(blob =>
        {
            var blobNameLower = blob.Name.ToLower();
            var score = keywords.Count(kw => blobNameLower.Contains(kw));
            return (blob, score);
        })
        .Where(x => x.score > 0)
        .OrderByDescending(x => x.score)
        .Take(5)
        .Select(x => x.blob)
        .ToList();

        // If no matches, return first 3 documents
        return scored.Count > 0 ? scored : blobs.Take(3).ToList();
    }
}
