using System.Net;
using System.Text.Json;
using FuncCosmicWwicUat.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace FuncCosmicWwicUat;

public class BlobFunctions
{
    private readonly BlobStorageService _blobStorage;
    private readonly ILogger<BlobFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public BlobFunctions(BlobStorageService blobStorage, ILogger<BlobFunctions> logger)
    {
        _blobStorage = blobStorage;
        _logger = logger;
    }

    // POST /api/upload?prefix=...&blob=...&overwrite=true|false
    [Function("UploadToBlob")]
    public async Task<HttpResponseData> UploadAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "upload")] HttpRequestData req)
    {
        try
        {
            var q = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var prefix = (q["prefix"] ?? "").Trim().Trim('/');
            var overwrite = string.Equals(q["overwrite"], "true", StringComparison.OrdinalIgnoreCase);

            if (!req.Headers.TryGetValues("Content-Type", out var ctValues))
                return await JsonError(req, HttpStatusCode.BadRequest, "Missing Content-Type header.");

            var reqContentType = ctValues.FirstOrDefault() ?? "";
            if (!reqContentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                return await JsonError(req, HttpStatusCode.BadRequest, "Expected multipart/form-data.");

            var mediaTypeHeader = MediaTypeHeaderValue.Parse(reqContentType);
            var boundary = HeaderUtilities.RemoveQuotes(mediaTypeHeader.Boundary).Value;
            if (string.IsNullOrWhiteSpace(boundary))
                return await JsonError(req, HttpStatusCode.BadRequest, "Missing multipart boundary.");

            var reader = new MultipartReader(boundary, req.Body);

            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync()) != null)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisp))
                    continue;

                var isFile =
                    contentDisp.DispositionType.Equals("form-data") &&
                    !string.IsNullOrWhiteSpace(contentDisp.FileName.Value);

                if (!isFile) continue;

                var fileName = contentDisp.FileName.Value?.Trim('"') ?? "upload.bin";

                var blobNameOverride = (q["blob"] ?? "").Trim();
                var blobName = string.IsNullOrWhiteSpace(blobNameOverride) ? fileName : blobNameOverride;

                var blobPath = string.IsNullOrWhiteSpace(prefix) ? blobName : $"{prefix}/{blobName}";

                var fileContentType = section.ContentType ?? "application/octet-stream";

                // Buffer into memory so we have a normal stream and can measure bytes
                await using var ms = new MemoryStream();
                await section.Body.CopyToAsync(ms);
                ms.Position = 0;

                var (container, uploadedPath, _) = await _blobStorage.UploadAsync(
                    blobPath,
                    ms,
                    fileContentType,
                    overwrite);

                var ok = req.CreateResponse(HttpStatusCode.OK);
                ok.Headers.Add("Content-Type", "application/json");
                await ok.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    status = "success",
                    storageAccountUrl = Environment.GetEnvironmentVariable("STORAGE_ACCOUNT_URL"),
                    container = container,
                    blob = uploadedPath,
                    bytes = ms.Length,
                    uploadedAtUtc = DateTime.UtcNow.ToString("O")
                }, JsonOpts));
                return ok;
            }

            return await JsonError(req, HttpStatusCode.BadRequest, "No file found in form-data. Use field name 'file'.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload failed");
            return await JsonError(req, HttpStatusCode.InternalServerError, ex.Message);
        }
    }

    // GET /api/blobs?prefix=...&limit=100
    [Function("ListBlobs")]
    public async Task<HttpResponseData> ListAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "blobs")] HttpRequestData req)
    {
        try
        {
            var q = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var prefix = (q["prefix"] ?? "").Trim();
            var limitStr = q["limit"] ?? "100";
            _ = int.TryParse(limitStr, out var limit);
            if (limit <= 0) limit = 100;
            if (limit > 500) limit = 500;

            var items = await _blobStorage.ListAsync(
                prefix: string.IsNullOrWhiteSpace(prefix) ? null : prefix,
                limit: limit);

            var resp = req.CreateResponse(HttpStatusCode.OK);
            resp.Headers.Add("Content-Type", "application/json");
            await resp.WriteStringAsync(JsonSerializer.Serialize(new
            {
                status = "success",
                storageAccountUrl = Environment.GetEnvironmentVariable("STORAGE_ACCOUNT_URL"),
                container = Environment.GetEnvironmentVariable("BLOB_CONTAINER_NAME"),
                prefix = prefix,
                countReturned = items.Count,
                items = items.Select(i => new
                {
                    name = i.Name,
                    lastModified = i.LastModified?.ToString("O"),
                    size = i.Size
                })
            }, JsonOpts));

            return resp;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List blobs failed");
            return await JsonError(req, HttpStatusCode.InternalServerError, ex.Message);
        }
    }

    private static async Task<HttpResponseData> JsonError(HttpRequestData req, HttpStatusCode code, string message)
    {
        var resp = req.CreateResponse(code);
        resp.Headers.Add("Content-Type", "application/json");
        await resp.WriteStringAsync(JsonSerializer.Serialize(new
        {
            status = "error",
            error = message
        }, JsonOpts));
        return resp;
    }
}