// ✅ SwaggerConfiguration.cs is RETAINED but no longer needed
// Swagger is configured directly in Program.cs — this file is safe to DELETE
// It is kept here for reference only and is never called

// File: SwaggerConfiguration.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

public static class SwaggerConfiguration
{
    public static void AddSwagger(IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "MCP Server API", Version = "v1" });
        });
    }
}