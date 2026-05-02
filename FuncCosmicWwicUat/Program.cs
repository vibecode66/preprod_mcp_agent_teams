using Azure.Identity;
using FuncCosmicWwicUat.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// ─── Logging ───────────────────────────────────────────────────────────────────
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Log environment for debugging
Console.WriteLine("=== FUNCTION APP STARTUP ===");
Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? "NOT SET"}");
Console.WriteLine($"AZURE_OPENAI_MODEL: {Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL") ?? "NOT SET"}");
Console.WriteLine($"AZURE_OPENAI_USE_MANAGED_IDENTITY: {Environment.GetEnvironmentVariable("AZURE_OPENAI_USE_MANAGED_IDENTITY") ?? "NOT SET"}");
Console.WriteLine($"MCP_SERVER_BASE_URL: {Environment.GetEnvironmentVariable("MCP_SERVER_BASE_URL") ?? "NOT SET"}");
Console.WriteLine($"BLOB_STORAGE_ACCOUNT: {Environment.GetEnvironmentVariable("BLOB_STORAGE_ACCOUNT") ?? "NOT SET"}");
Console.WriteLine($"BLOB_CONTAINER_NAME: {Environment.GetEnvironmentVariable("BLOB_CONTAINER_NAME") ?? "NOT SET"}");
Console.WriteLine("============================");

// ─── UAMI: uami-wwic-uat ──────────────────────────────────────────────────────
// Used for: Azure OpenAI + MCP Server + Blob Storage calls
//
// IMPORTANT:
// - Set AZURE_OPENAI_MANAGED_IDENTITY_CLIENT_ID to the *Client ID GUID* of the UAMI.
// - (Do NOT set it to the resourceId string.)
builder.Services.AddSingleton<ManagedIdentityCredential>(_ =>
{
    var clientId = Environment.GetEnvironmentVariable("AZURE_OPENAI_MANAGED_IDENTITY_CLIENT_ID");

    if (string.IsNullOrWhiteSpace(clientId))
    {
        // NOTE: this falls back to System Managed Identity (not UAMI).
        // If you want "UAMI only", keep this var set.
        Console.WriteLine("WARNING: AZURE_OPENAI_MANAGED_IDENTITY_CLIENT_ID not set. " +
                          "Falling back to System Managed Identity.");
        return new ManagedIdentityCredential();
    }

    Console.WriteLine($"UAMI loaded (clientId: {clientId})");
    return new ManagedIdentityCredential(clientId);
});

// ─── MCP HTTP Client ──────────────────────────────────────────────────────────
builder.Services.AddHttpClient<McpClientService>(client =>
{
    var mcpBaseUrl = Environment.GetEnvironmentVariable("MCP_SERVER_BASE_URL")
        ?? "https://app-cosmic-wwic-uat-eastus.azurewebsites.net";
    client.BaseAddress = new Uri(mcpBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
});

// ─── Azure OpenAI Responses API Client ─────────────────────────────────────────
builder.Services.AddHttpClient<AzureOpenAIResponsesClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

// ─── Application Services ─────────────────────────────────────────────────────
builder.Services.AddSingleton<IntentService>();
builder.Services.AddSingleton<NL2SqlService>();
builder.Services.AddSingleton<ProcessMiningService>();

// ✅ NEW: Blob upload/list service (uses the same ManagedIdentityCredential / UAMI)
builder.Services.AddSingleton<BlobStorageService>();

try
{
    builder.Build().Run();
}
catch (Exception ex)
{
    Console.WriteLine("FATAL ERROR during startup:");
    Console.WriteLine(ex.Message);
    Console.WriteLine(ex.StackTrace);
    throw;
}

// using Azure.Identity;
// using FuncCosmicWwicUat.Services;
// using Microsoft.Azure.Functions.Worker;
// using Microsoft.Azure.Functions.Worker.Builder;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Hosting;
// using Microsoft.Extensions.Logging;

// var builder = FunctionsApplication.CreateBuilder(args);

// builder.ConfigureFunctionsWebApplication();

// // ─── Logging ───────────────────────────────────────────────────────────────────
// builder.Logging.AddConsole();
// builder.Logging.SetMinimumLevel(LogLevel.Information);

// // Log environment for debugging
// Console.WriteLine("=== FUNCTION APP STARTUP ===");
// Console.WriteLine($"AZURE_OPENAI_ENDPOINT: {Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? "NOT SET"}");
// Console.WriteLine($"AZURE_OPENAI_MODEL: {Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL") ?? "NOT SET"}");
// Console.WriteLine($"AZURE_OPENAI_USE_MANAGED_IDENTITY: {Environment.GetEnvironmentVariable("AZURE_OPENAI_USE_MANAGED_IDENTITY") ?? "NOT SET"}");
// Console.WriteLine($"MCP_SERVER_BASE_URL: {Environment.GetEnvironmentVariable("MCP_SERVER_BASE_URL") ?? "NOT SET"}");
// Console.WriteLine("============================");

// // ─── UAMI: uami-wwic-uat ──────────────────────────────────────────────────────
// // Client ID: 1e8f357b-bec7-41de-8965-471188c16a13
// // Used for: Azure OpenAI + MCP Server (App Service) calls
// // Env var:  AZURE_OPENAI_MANAGED_IDENTITY_CLIENT_ID
// builder.Services.AddSingleton<ManagedIdentityCredential>(sp =>
// {
//     var clientId = Environment.GetEnvironmentVariable("AZURE_OPENAI_MANAGED_IDENTITY_CLIENT_ID");

//     if (string.IsNullOrWhiteSpace(clientId))
//     {
//         Console.WriteLine("WARNING: AZURE_OPENAI_MANAGED_IDENTITY_CLIENT_ID not set. " +
//                           "Falling back to System Managed Identity.");
//         return new ManagedIdentityCredential();
//     }

//     Console.WriteLine($"UAMI loaded: uami-wwic-uat (clientId: {clientId})");
//     return new ManagedIdentityCredential(clientId);
// });

// // ─── MCP HTTP Client ──────────────────────────────────────────────────────────
// builder.Services.AddHttpClient<McpClientService>(client =>
// {
//     var mcpBaseUrl = Environment.GetEnvironmentVariable("MCP_SERVER_BASE_URL")
//         ?? "https://app-cosmic-wwic-uat-eastus.azurewebsites.net";
//     client.BaseAddress = new Uri(mcpBaseUrl);
//     client.Timeout = TimeSpan.FromSeconds(60);
// });

// // ─── Azure OpenAI Responses API Client ─────────────────────────────────────────
// builder.Services.AddHttpClient<AzureOpenAIResponsesClient>(client =>
// {
//     client.Timeout = TimeSpan.FromSeconds(60);
// });

// // ─── Application Services ─────────────────────────────────────────────────────
// builder.Services.AddSingleton<IntentService>();
// builder.Services.AddSingleton<NL2SqlService>();

// try
// {
//     builder.Build().Run();
// }
// catch (Exception ex)
// {
//     Console.WriteLine("FATAL ERROR during startup:");
//     Console.WriteLine(ex.Message);
//     Console.WriteLine(ex.StackTrace);
//     throw;
// }