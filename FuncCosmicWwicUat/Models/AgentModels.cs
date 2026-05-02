namespace FuncCosmicWwicUat.Models;

// ─── Inbound request from Teams / Copilot ─────────────────────────────────────
public class AgentRequest
{
    public string Message { get; set; } = string.Empty;
    public string? SessionId { get; set; }
}

// ─── Outbound response to Teams / Copilot ─────────────────────────────────────
public class AgentResponse
{
    public string Status { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public string Reply { get; set; } = string.Empty;
    public string? GeneratedSql { get; set; }
    public int? RowCount { get; set; }
    public object? Rows { get; set; }
}

// ─── Intent detection result ──────────────────────────────────────────────────
public class IntentResult
{
    public string Intent { get; set; } = string.Empty;   // "NL2SQL" | "GENERAL"
    public string Reasoning { get; set; } = string.Empty;
}

// ─── MCP execute request/response ─────────────────────────────────────────────
public class McpExecuteRequest
{
    public string SqlQuery { get; set; } = string.Empty;
}

public class McpExecuteResponse
{
    public string Status { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public List<Dictionary<string, object?>>? Rows { get; set; }
    public string? Message { get; set; }
}

public class McpSchemaTable
{
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

// ─── Blob Storage Models ──────────────────────────────────────────────────────
public class BlobDocument
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Modified { get; set; } = string.Empty;
}

public class BlobListResponse
{
    public string Status { get; set; } = string.Empty;
    public List<BlobDocument> Blobs { get; set; } = new List<BlobDocument>();
}