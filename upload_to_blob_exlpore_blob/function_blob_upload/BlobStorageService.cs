using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace FuncCosmicWwicUat.Services;

public class BlobStorageService
{
    private readonly BlobServiceClient _blobService;

    private readonly string _accountUrl;
    private readonly string _containerName;

    public BlobStorageService(ManagedIdentityCredential credential)
    {
        _accountUrl = Environment.GetEnvironmentVariable("STORAGE_ACCOUNT_URL")
            ?? throw new InvalidOperationException("STORAGE_ACCOUNT_URL is required.");

        _containerName = Environment.GetEnvironmentVariable("BLOB_CONTAINER_NAME")
            ?? throw new InvalidOperationException("BLOB_CONTAINER_NAME is required.");

        _blobService = new BlobServiceClient(new Uri(_accountUrl), credential);
    }

    public BlobContainerClient GetContainer() => _blobService.GetBlobContainerClient(_containerName);

    public async Task<(string Container, string BlobPath, long Bytes)> UploadAsync(
        string blobPath,
        Stream content,
        string contentType,
        bool overwrite)
    {
        var container = GetContainer();
        await container.CreateIfNotExistsAsync(PublicAccessType.None);

        var blob = container.GetBlobClient(blobPath);

        if (overwrite)
        {
            await blob.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots);
        }
        else
        {
            if (await blob.ExistsAsync())
                throw new InvalidOperationException($"Blob already exists: {blobPath}");
        }

        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = string.IsNullOrWhiteSpace(contentType)
                    ? "application/octet-stream"
                    : contentType
            }
        };

        // Upload once (no retries that require rewinding the stream)
        await blob.UploadAsync(content, options);

        long bytes = -1;
        try
        {
            if (content.CanSeek) bytes = content.Length;
        }
        catch { /* ignore */ }

        return (_containerName, blobPath, bytes);
    }

    public async Task<List<(string Name, DateTimeOffset? LastModified, long? Size)>> ListAsync(string? prefix, int limit)
    {
        var container = GetContainer();
        var results = new List<(string, DateTimeOffset?, long?)>();

        await foreach (var item in container.GetBlobsAsync(prefix: prefix))
        {
            results.Add((item.Name, item.Properties.LastModified, item.Properties.ContentLength));
            if (results.Count >= limit) break;
        }

        return results;
    }
}