# Azure Blob Storage Integration with MCP Server

## Overview
The MCP Server now includes full Azure Blob Storage support, similar to how SQL Server is integrated. It uses Managed Identity (UAMI) for authentication and exposes 6 tools for blob operations.

## Required Environment Variables

Set these in your App Service Configuration:

| Variable | Value | Description |
|----------|-------|-------------|
| `BLOB_STORAGE_ACCOUNT` | `<storage-account-name>` | Storage account name (without .blob.core.windows.net) |
| `BLOB_MANAGED_IDENTITY_CLIENT_ID` | `<uami-client-id>` | (Optional) UAMI Client ID for Blob Storage access |

**Note:** If `BLOB_MANAGED_IDENTITY_CLIENT_ID` is not set, the system will use the default Managed Identity.

## Available MCP Tools

### 1. `blob_health_check()`
Verifies connectivity to Azure Blob Storage.

**Response:**
```json
{
  "status": "healthy",
  "message": "Connected to Blob Storage"
}
```

### 2. `list_blob_containers()`
Lists all containers in the storage account.

**Response:**
```json
{
  "status": "success",
  "containers": [
    {
      "name": "my-container",
      "created": "2026-01-15T10:30:00.000Z"
    }
  ]
}
```

### 3. `list_blobs_in_container(containerName: string)`
Lists all blobs in a specific container.

**Parameters:**
- `containerName` - Name of the container

**Response:**
```json
{
  "status": "success",
  "blobs": [
    {
      "name": "file.txt",
      "size": 1024,
      "modified": "2026-01-15T10:30:00.000Z"
    }
  ]
}
```

### 4. `upload_blob(containerName: string, blobName: string, filePath: string)`
Uploads a local file to blob storage.

**Parameters:**
- `containerName` - Target container name
- `blobName` - Name for the blob in storage
- `filePath` - Local file path to upload

**Response:**
```json
{
  "status": "success",
  "message": "Uploaded file.txt to my-container"
}
```

### 5. `download_blob(containerName: string, blobName: string, outputPath: string)`
Downloads a blob from storage to a local file.

**Parameters:**
- `containerName` - Source container name
- `blobName` - Name of the blob in storage
- `outputPath` - Local file path to save to

**Response:**
```json
{
  "status": "success",
  "message": "Downloaded file.txt to /path/to/output.txt"
}
```

### 6. `delete_blob(containerName: string, blobName: string)`
Deletes a blob from storage.

**Parameters:**
- `containerName` - Container name
- `blobName` - Blob name to delete

**Response:**
```json
{
  "status": "success",
  "message": "Deleted file.txt from my-container"
}
```

## Setup Steps

### 1. Deploy the Updated MCP Server
The `MCPServer.csproj` has been updated with the `Azure.Storage.Blobs` NuGet package (v12.22.2).

### 2. Configure Azure App Service
1. Go to your App Service → **Configuration**
2. Add application settings:
   - `BLOB_STORAGE_ACCOUNT`: Your storage account name
   - `BLOB_MANAGED_IDENTITY_CLIENT_ID`: (Optional) UAMI Client ID
3. Save and restart the App Service

### 3. Grant UAMI Access to Blob Storage
On the storage account:
1. Go to **Access Control (IAM)**
2. Click **+ Add role assignment**
3. Select role: **Storage Blob Data Contributor** (or appropriate role)
4. Assign to: **Managed Identity**
5. Select your UAMI (uami-wwic-uat)

## Usage Examples

### Check Connection
```
GET /mcp/tools/blob_health_check
```

### List Containers
```
GET /mcp/tools/list_blob_containers
```

### Upload a File
```
POST /mcp/tools/upload_blob
{
  "containerName": "documents",
  "blobName": "report.pdf",
  "filePath": "/local/path/report.pdf"
}
```

### Download a File
```
POST /mcp/tools/download_blob
{
  "containerName": "documents",
  "blobName": "report.pdf",
  "outputPath": "/local/output.pdf"
}
```

## Architecture

The Blob Storage integration follows the same pattern as SQL Server:

1. **Managed Identity Authentication** - Uses Azure.Identity to authenticate with UAMI
2. **MCP Tool Attributes** - Each operation is exposed as an `[McpServerTool]`
3. **Error Handling** - Consistent JSON error responses
4. **Configuration** - Environment variables for flexibility

## NuGet Dependencies

- `Azure.Storage.Blobs` (v12.22.2) - Blob storage client library
- `Azure.Identity` (v1.13.2) - Managed Identity support

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "BLOB_STORAGE_ACCOUNT is not set" | Add environment variable to App Service Configuration |
| 403 Forbidden errors | Verify UAMI has "Storage Blob Data Contributor" role on storage account |
| Connection timeout | Check network security groups and firewall rules |
| File not found (upload) | Verify local file path exists and is accessible |
