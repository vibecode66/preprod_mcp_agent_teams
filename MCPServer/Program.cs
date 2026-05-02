using System.Text.Json;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using ModelContextProtocol.Server;
using ModelContextProtocol.AspNetCore;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

// ─── Host setup ────────────────────────────────────────────────────────────────
Console.WriteLine("Starting POC MCP Server for Azure SQL...");
var builder = WebApplication.CreateBuilder(args);

// ─── UAMI — Register shared credential for all Azure SDK calls ────────────────
// uami-wwic-uat Client ID must be set in App Service → Configuration
// Key: SQL_MANAGED_IDENTITY_CLIENT_ID
// Value: <client-id-of-uami-wwic-uat>
var uamiClientId = Environment.GetEnvironmentVariable("SQL_MANAGED_IDENTITY_CLIENT_ID");
if (string.IsNullOrWhiteSpace(uamiClientId))
{
    Console.WriteLine("WARNING: SQL_MANAGED_IDENTITY_CLIENT_ID is not set. " +
                      "Falling back to System Managed Identity or ambient credential.");
}
else
{
    Console.WriteLine($"UAMI Client ID loaded: {uamiClientId}");
}

// ─── Blob Storage Configuration ────────────────────────────────────────────────
var blobStorageAccount = Environment.GetEnvironmentVariable("BLOB_STORAGE_ACCOUNT");
var blobMiClientId = Environment.GetEnvironmentVariable("BLOB_MANAGED_IDENTITY_CLIENT_ID");
if (string.IsNullOrWhiteSpace(blobStorageAccount))
{
    Console.WriteLine("WARNING: BLOB_STORAGE_ACCOUNT is not set. Blob Storage tools will be available but require configuration.");
}
else
{
    Console.WriteLine($"Blob Storage Account configured: {blobStorageAccount}");
    if (!string.IsNullOrWhiteSpace(blobMiClientId))
        Console.WriteLine($"Using UAMI for Blob Storage: {blobMiClientId}");
}

// ✅ Register ManagedIdentityCredential as singleton — used by any future Azure SDK services
builder.Services.AddSingleton<ManagedIdentityCredential>(_ =>
    string.IsNullOrWhiteSpace(uamiClientId)
        ? new ManagedIdentityCredential()
        : new ManagedIdentityCredential(uamiClientId));

builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();
builder.Services.AddControllers();

// ─── CORS ──────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// ─── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "MCP Server API", Version = "v1" });
});

var app = builder.Build();

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MCP Server API v1");
});

app.MapControllers();
app.MapMcp();

app.Run();


// ─── MCP Tools ─────────────────────────────────────────────────────────────────
[McpServerToolType]
public static class SqlTools
{
    static readonly string ConnStr = InitConnStr();

    static string InitConnStr()
    {
        var sqlServer  = Environment.GetEnvironmentVariable("SQL_SERVER");
        var sqlDb      = Environment.GetEnvironmentVariable("SQL_DATABASE");

        // ✅ UAMI: uami-wwic-uat — Client ID passed via SQL_MANAGED_IDENTITY_CLIENT_ID
        var miClientId = Environment.GetEnvironmentVariable("SQL_MANAGED_IDENTITY_CLIENT_ID");

        if (string.IsNullOrEmpty(sqlServer) || string.IsNullOrEmpty(sqlDb))
            throw new InvalidOperationException(
                "SQL_SERVER and SQL_DATABASE environment variables must be set.");

        // ✅ Base connection string — uses Active Directory Default
        //    which honours UAMI when User Id is supplied
        var str = $"Server=tcp:{sqlServer},1433;Database={sqlDb};" +
                  "Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;" +
                  "Authentication=Active Directory Default;";

        // ✅ Append UAMI Client ID — without this it falls back to System MI
        if (!string.IsNullOrWhiteSpace(miClientId))
        {
            str += $"User Id={miClientId};";
            Console.WriteLine($"SQL connection using UAMI: uami-wwic-uat ({miClientId})");
        }
        else
        {
            Console.WriteLine("WARNING: SQL_MANAGED_IDENTITY_CLIENT_ID not set. " +
                              "Using System Managed Identity for SQL.");
        }

        return str;
    }

    [McpServerTool, Description("sql_health_check")]
    public static async Task<string> SqlHealthCheck()
    {
        try
        {
            using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            var result = await cmd.ExecuteScalarAsync();
            return JsonSerializer.Serialize(new { status = "healthy", value = result });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
        }
    }

    [McpServerTool, Description("list_tables")]
    public static async Task<string> ListTables()
    {
        try
        {
            using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE " +
                              "FROM INFORMATION_SCHEMA.TABLES " +
                              "ORDER BY TABLE_SCHEMA, TABLE_NAME";
            using var reader = await cmd.ExecuteReaderAsync();
            var results = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync())
            {
                results.Add(new Dictionary<string, object?>
                {
                    ["schema"] = reader.GetString(0),
                    ["name"]   = reader.GetString(1),
                    ["type"]   = reader.GetString(2)
                });
            }
            return JsonSerializer.Serialize(results);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
        }
    }

    [McpServerTool, Description("execute_readonly_sql")]
    public static async Task<string> ExecuteReadonlySql(string sqlQuery)
    {
        try
        {
            if (!IsSafeSql(sqlQuery, out var error))
                return JsonSerializer.Serialize(new { status = "error", message = error });

            using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sqlQuery;
            using var reader = await cmd.ExecuteReaderAsync();
            var rows = ReadRows(reader);
            return JsonSerializer.Serialize(new { status = "success", rowCount = rows.Count, rows });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
        }
    }

    static bool IsSafeSql(string query, out string error)
    {
        if (string.IsNullOrWhiteSpace(query) ||
            !query.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            error = "Only SELECT queries are allowed.";
            return false;
        }

        var blocked = new[] { "insert", "update", "delete", "drop", "alter",
                               "truncate", "merge", "exec", "execute", "create" };
        var q = query.ToLowerInvariant();
        foreach (var word in blocked)
        {
            if (Regex.IsMatch(q, $@"\b{word}\b"))
            {
                error = $"Blocked SQL keyword detected: {word.ToUpper()}";
                return false;
            }
        }

        var qWithoutTrailing = q.TrimEnd(';');
        if (qWithoutTrailing.Contains(';'))
        {
            error = "Multiple SQL statements are not allowed.";
            return false;
        }

        error = "";
        return true;
    }

    static List<Dictionary<string, object?>> ReadRows(SqlDataReader reader)
    {
        var cols = Enumerable.Range(0, reader.FieldCount)
                             .Select(reader.GetName).ToArray();
        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var d = new Dictionary<string, object?>();
            for (int i = 0; i < cols.Length; i++)
            {
                var v = reader.IsDBNull(i) ? null : reader.GetValue(i);
                d[cols[i]] = v switch
                {
                    DateTime dt        => dt.ToString("O"),
                    DateTimeOffset dto => dto.ToString("O"),
                    byte[] bytes       => Convert.ToBase64String(bytes),
                    Guid g             => g.ToString(),
                    _                  => v
                };
            }
            rows.Add(d);
        }
        return rows;
    }
}

// ─── MCP Tools for Azure Blob Storage ──────────────────────────────────────────
[McpServerToolType]
public static class BlobStorageTools
{
    static readonly ManagedIdentityCredential Credential = new(
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BLOB_MANAGED_IDENTITY_CLIENT_ID"))
            ? null
            : Environment.GetEnvironmentVariable("BLOB_MANAGED_IDENTITY_CLIENT_ID"));

    static string GetBlobUri()
    {
        var account = Environment.GetEnvironmentVariable("BLOB_STORAGE_ACCOUNT");
        if (string.IsNullOrEmpty(account))
            throw new InvalidOperationException("BLOB_STORAGE_ACCOUNT environment variable must be set.");
        return $"https://{account}.blob.core.windows.net";
    }

    [McpServerTool, Description("blob_health_check")]
    public static async Task<string> BlobHealthCheck()
    {
        try
        {
            var blobUri = GetBlobUri();
            var client = new BlobContainerClient(new Uri($"{blobUri}/"), Credential);
            await client.GetPropertiesAsync();
            return JsonSerializer.Serialize(new { status = "healthy", message = "Connected to Blob Storage" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
        }
    }

    [McpServerTool, Description("list_blob_containers")]
    public static async Task<string> ListBlobContainers()
    {
        try
        {
            var blobUri = GetBlobUri();
            var client = new BlobServiceClient(new Uri(blobUri), Credential);
            var containers = new List<Dictionary<string, object?>>();
            await foreach (var container in client.GetBlobContainersAsync())
            {
                containers.Add(new Dictionary<string, object?>
                {
                    ["name"] = container.Name,
                    ["created"] = DateTime.UtcNow.ToString("O")
                });
            }
            return JsonSerializer.Serialize(new { status = "success", containers });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
        }
    }

    [McpServerTool, Description("list_blobs_in_container")]
    public static async Task<string> ListBlobsInContainer(string containerName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(containerName))
                return JsonSerializer.Serialize(new { status = "error", message = "Container name is required." });

            var blobUri = GetBlobUri();
            var client = new BlobContainerClient(new Uri($"{blobUri}/{containerName}"), Credential);
            var blobs = new List<Dictionary<string, object?>>();
            await foreach (var blob in client.GetBlobsAsync())
            {
                blobs.Add(new Dictionary<string, object?>
                {
                    ["name"] = blob.Name,
                    ["size"] = blob.Properties.ContentLength,
                    ["modified"] = blob.Properties.LastModified?.ToString("O") ?? "N/A"
                });
            }
            return JsonSerializer.Serialize(new { status = "success", blobs });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
        }
    }

    [McpServerTool, Description("upload_blob")]
    public static async Task<string> UploadBlob(string containerName, string blobName, string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(containerName) || string.IsNullOrWhiteSpace(blobName))
                return JsonSerializer.Serialize(new { status = "error", message = "Container and blob names are required." });

            if (!File.Exists(filePath))
                return JsonSerializer.Serialize(new { status = "error", message = $"File not found: {filePath}" });

            var blobUri = GetBlobUri();
            var client = new BlobClient(new Uri($"{blobUri}/{containerName}/{blobName}"), Credential);
            using var stream = File.OpenRead(filePath);
            await client.UploadAsync(stream, overwrite: true);
            return JsonSerializer.Serialize(new { status = "success", message = $"Uploaded {blobName} to {containerName}" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
        }
    }

    [McpServerTool, Description("download_blob")]
    public static async Task<string> DownloadBlob(string containerName, string blobName, string outputPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(containerName) || string.IsNullOrWhiteSpace(blobName))
                return JsonSerializer.Serialize(new { status = "error", message = "Container and blob names are required." });

            var blobUri = GetBlobUri();
            var client = new BlobClient(new Uri($"{blobUri}/{containerName}/{blobName}"), Credential);
            var download = await client.DownloadAsync();
            using var fileStream = File.Create(outputPath);
            await download.Value.Content.CopyToAsync(fileStream);
            return JsonSerializer.Serialize(new { status = "success", message = $"Downloaded {blobName} to {outputPath}" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
        }
    }

    [McpServerTool, Description("download_blob_as_text")]
    public static async Task<string> DownloadBlobAsText(string containerName, string blobName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(containerName) || string.IsNullOrWhiteSpace(blobName))
                return JsonSerializer.Serialize(new { status = "error", message = "Container and blob names are required." });

            var blobUri = GetBlobUri();
            var client = new BlobClient(new Uri($"{blobUri}/{containerName}/{blobName}"), Credential);
            var download = await client.DownloadAsync();
            using var streamReader = new System.IO.StreamReader(download.Value.Content);
            var content = await streamReader.ReadToEndAsync();
            return JsonSerializer.Serialize(new { status = "success", content });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
        }
    }

    [McpServerTool, Description("delete_blob")]
    public static async Task<string> DeleteBlob(string containerName, string blobName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(containerName) || string.IsNullOrWhiteSpace(blobName))
                return JsonSerializer.Serialize(new { status = "error", message = "Container and blob names are required." });

            var blobUri = GetBlobUri();
            var client = new BlobClient(new Uri($"{blobUri}/{containerName}/{blobName}"), Credential);
            await client.DeleteAsync();
            return JsonSerializer.Serialize(new { status = "success", message = $"Deleted {blobName} from {containerName}" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
        }
    }
}