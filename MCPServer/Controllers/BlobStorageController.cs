using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace MCPServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BlobStorageController : ControllerBase
    {
        /// <summary>
        /// Health check for Blob Storage connectivity
        /// GET /api/blobstorage/health
        /// </summary>
        [HttpGet("health")]
        public async Task<IActionResult> BlobHealthCheck()
        {
            var result = await BlobStorageTools.BlobHealthCheck();
            return Content(result, "application/json");
        }

        /// <summary>
        /// List all blob containers
        /// GET /api/blobstorage/containers
        /// </summary>
        [HttpGet("containers")]
        public async Task<IActionResult> ListContainers()
        {
            var result = await BlobStorageTools.ListBlobContainers();
            return Content(result, "application/json");
        }

        /// <summary>
        /// List blobs in a specific container
        /// POST /api/blobstorage/list
        /// Body: { "containerName": "container-name" }
        /// </summary>
        [HttpPost("list")]
        public async Task<IActionResult> ListBlobs([FromBody] ListBlobsRequest request)
        {
            if (string.IsNullOrEmpty(request?.ContainerName))
                return BadRequest(new { error = "containerName is required" });

            var result = await BlobStorageTools.ListBlobsInContainer(request.ContainerName);
            return Content(result, "application/json");
        }

        /// <summary>
        /// Download a blob as text
        /// POST /api/blobstorage/download
        /// Body: { "containerName": "container-name", "blobName": "blob-name" }
        /// </summary>
        [HttpPost("download")]
        public async Task<IActionResult> DownloadBlob([FromBody] DownloadBlobRequest request)
        {
            if (string.IsNullOrEmpty(request?.ContainerName) || string.IsNullOrEmpty(request?.BlobName))
                return BadRequest(new { error = "containerName and blobName are required" });

            var result = await BlobStorageTools.DownloadBlobAsText(request.ContainerName, request.BlobName);
            return Content(result, "application/json");
        }

        /// <summary>
        /// Upload a blob from text content
        /// POST /api/blobstorage/upload
        /// Body: { "containerName": "container-name", "blobName": "blob-name", "content": "blob content" }
        /// </summary>
        [HttpPost("upload")]
        public async Task<IActionResult> UploadBlob([FromBody] UploadBlobRequest request)
        {
            if (string.IsNullOrEmpty(request?.ContainerName) || string.IsNullOrEmpty(request?.BlobName))
                return BadRequest(new { error = "containerName and blobName are required" });

            // For now, we'll skip the file-based upload since we're working with text content
            // This would need a separate implementation to handle binary files
            return BadRequest(new { error = "Use the API endpoint directly or upload via Azure Storage SDK" });
        }

        /// <summary>
        /// Delete a blob
        /// DELETE /api/blobstorage/delete
        /// Body: { "containerName": "container-name", "blobName": "blob-name" }
        /// </summary>
        [HttpPost("delete")]
        public async Task<IActionResult> DeleteBlob([FromBody] DeleteBlobRequest request)
        {
            if (string.IsNullOrEmpty(request?.ContainerName) || string.IsNullOrEmpty(request?.BlobName))
                return BadRequest(new { error = "containerName and blobName are required" });

            var result = await BlobStorageTools.DeleteBlob(request.ContainerName, request.BlobName);
            return Content(result, "application/json");
        }
    }

    public class ListBlobsRequest
    {
        public string ContainerName { get; set; }
    }

    public class DownloadBlobRequest
    {
        public string ContainerName { get; set; }
        public string BlobName { get; set; }
    }

    public class UploadBlobRequest
    {
        public string ContainerName { get; set; }
        public string BlobName { get; set; }
        public string Content { get; set; }
    }

    public class DeleteBlobRequest
    {
        public string ContainerName { get; set; }
        public string BlobName { get; set; }
    }
}
