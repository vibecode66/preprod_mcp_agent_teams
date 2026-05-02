# Process Mining Intent Routing Implementation

## 🎯 Overview

The Function App has been enhanced to support **Process Mining** queries. When users ask about process mining, workflows, or process analysis, the system now:

1. **Detects** the "PROCESS_MINING" intent
2. **Routes** to the MCP Server's Blob Storage
3. **Retrieves** process mining documents from Azure Blob Storage
4. **Analyzes** documents using Azure OpenAI LLM
5. **Returns** formatted process insights

## 🏗️ Architecture

```
User Query
    ↓
Intent Detection (Enhanced)
    ↓
    ├─ NL2SQL → SQL Server → Query Results
    ├─ PROCESS_MINING → MCP Server → Blob Storage → LLM Analysis → Process Insights
    └─ GENERAL → OpenAI Chat → Response
```

## 📝 Modified Files

### 1. **IntentService.cs** (Enhanced)
- Added "PROCESS_MINING" as new intent classification option
- Updated intent classifier prompt to recognize process mining keywords
- Keywords: "process mining", "process flow", "workflow", "process discovery", etc.

### 2. **McpClientService.cs** (Enhanced)
New methods for Blob Storage operations:
- `ListBlobsAsync(containerName)` - List blobs in a container
- `DownloadBlobAsStringAsync(containerName, blobName)` - Download and read blob content
- `BlobHealthCheckAsync()` - Health check for blob storage

### 3. **ProcessMiningService.cs** (NEW)
New service handling all process mining logic:
- `SearchDocumentsAsync(userQuery, containerName)` - Search & filter relevant documents
- `ExtractProcessDetailsAsync(documents, userQuery, containerName)` - Use LLM to extract process info
- `FormatProcessMiningReplyAsync(analysisResult, userQuery)` - Format response nicely
- Keyword extraction and document filtering logic

### 4. **AgentFunction.cs** (Enhanced)
- Added `ProcessMiningService` dependency
- New `HandleProcessMiningAsync()` method
- Updated routing logic to handle PROCESS_MINING intent
- Flow: Search → Analyze → Format → Return

### 5. **AgentModels.cs** (Enhanced)
New models for Blob Storage:
- `BlobDocument` - Represents a blob file
- `BlobListResponse` - Response from blob list operation

### 6. **Program.cs** (Enhanced)
- Registered `ProcessMiningService` in dependency injection
- Added logging for Blob Storage configuration

## 🔧 Configuration Required

### Environment Variables
Set these in your Function App configuration:

| Variable | Value | Example |
|----------|-------|---------|
| `BLOB_STORAGE_ACCOUNT` | Storage account name | `myblob` |
| `BLOB_CONTAINER_NAME` | Container name | `process-mining` |
| `BLOB_MANAGED_IDENTITY_CLIENT_ID` | (Optional) UAMI Client ID | `{client-id-guid}` |
| `MCP_SERVER_BASE_URL` | MCP Server URL | `https://app-cosmic-wwic-uat-eastus.azurewebsites.net` |

### Azure Setup

1. **Create Blob Container** (if not exists)
   ```bash
   az storage container create \
     --name process-mining \
     --account-name <storage-account>
   ```

2. **Upload Process Documents**
   - Text files (.txt, .md, .json)
   - Documents should contain process descriptions, workflows, or process mining results
   - Example: `order-fulfillment-process.txt`, `claims-workflow.json`

3. **Grant UAMI Access**
   - Go to Storage Account → Access Control (IAM)
   - Add role assignment: **Storage Blob Data Reader** (or Contributor)
   - Assign to UAMI (uami-wwic-uat)

## 🔄 Request/Response Flow

### Request
```json
{
  "message": "Show me the process mining for order fulfillment"
}
```

### Processing Steps
1. Intent classifier → "PROCESS_MINING"
2. Search for documents with keywords: ["order", "fulfillment"]
3. Download matching documents from blob storage
4. Use LLM to extract process information
5. Format response

### Response
```json
{
  "status": "success",
  "intent": "PROCESS_MINING",
  "reply": "The order fulfillment process includes the following steps:\n1. Order received and validated...",
  "rows": [
    {
      "name": "order-fulfillment-process.txt",
      "size": 2048,
      "modified": "2026-01-15T10:30:00Z"
    }
  ]
}
```

## 🧪 Test Cases

### Test 1: Basic Process Mining Query
**Input:**
```json
{ "message": "What is the process mining analysis for claims processing?" }
```

**Expected:**
- Intent: PROCESS_MINING
- Retrieves documents containing "claims" or "processing"
- Returns formatted process analysis

### Test 2: No Documents
**Input:**
```json
{ "message": "Show me blockchain mining process" }
```

**Expected:**
- Intent: PROCESS_MINING
- No matching documents found
- Returns: "No process mining documents found for your query."

### Test 3: Multiple Relevant Documents
**Input:**
```json
{ "message": "Analyze the workflow for customer onboarding" }
```

**Expected:**
- Intent: PROCESS_MINING
- Searches for "workflow" and "onboarding"
- Downloads up to 5 matching documents
- Uses LLM to consolidate and extract relevant information
- Returns comprehensive process analysis

## 📊 Data Flow Example

```
User Query: "What is the process mining for order to cash?"
    ↓
IntentService.DetectIntent()
    → Returns: {intent: "PROCESS_MINING", reasoning: "User asks for process mining analysis"}
    ↓
HandleProcessMiningAsync()
    ↓
ProcessMiningService.SearchDocumentsAsync("order to cash")
    → Keywords: ["order", "cash"]
    → Lists blobs in "process-mining" container
    → Filters: o2c-process.json, order-cash-flow.txt, ...
    → Returns: [BlobDocument(name: "o2c-process.json", size: 5120, ...)]
    ↓
ProcessMiningService.ExtractProcessDetailsAsync([o2c-process.json], query)
    → McpClientService.DownloadBlobAsStringAsync("process-mining", "o2c-process.json")
    → LLM Prompt: "Analyze this process mining document and answer: What is the process mining for order to cash?"
    → Returns: "The order-to-cash process consists of 6 main steps:..."
    ↓
ProcessMiningService.FormatProcessMiningReplyAsync(analysis, query)
    → LLM Prompt: "Format this analysis as a clear response"
    → Returns: Formatted markdown-style response
    ↓
AgentFunction returns: {status: "success", intent: "PROCESS_MINING", reply: "...", rows: [...]}
```

## ⚙️ Key Features

### Smart Document Filtering
- Extracts keywords from user query (stops words removed)
- Ranks documents by keyword match score
- Returns top 5 matching documents
- Falls back to first 3 if no keyword matches

### Document Limits
- Downloads max 5 documents per query
- Limits content to first 2000 chars per document
- Prevents token limit issues with LLM

### Error Handling
- Graceful fallback if document download fails
- Returns raw analysis if formatting fails
- Comprehensive error logging

### Performance
- Documents cached during analysis
- Async/await throughout
- 60-second timeout for Blob operations

## 🔍 Debugging

### Enable Logging
Function App logs show:
- Intent detection result
- Document search results count
- Document download attempts
- LLM analysis status

### Sample Logs
```
[INFO] Intent: PROCESS_MINING — User asks for process flow analysis
[INFO] Found 3 process mining documents
[INFO] Downloaded document: workflow-001.json (1850 chars)
[INFO] Process mining analysis completed
```

### Troubleshooting

| Issue | Solution |
|-------|----------|
| Intent not recognized | Ensure keywords match prompt (process mining, workflow, flow, etc.) |
| No documents found | Upload documents to `process-mining` container |
| 403 Forbidden from Blob | Check UAMI has Storage Blob Data Reader role |
| LLM analysis fails | Check Azure OpenAI connectivity and token limits |
| Timeout | Increase MCP Server timeout or reduce document size |

## 🚀 Deployment

### 1. Build and Test Locally
```bash
cd FuncCosmicWwicUat
dotnet build
dotnet test
```

### 2. Update MCP Server (if not done)
Ensure MCP Server has been deployed with Blob Storage tools:
```bash
cd MCPServer
dotnet publish -c Release -o ./publish
# Deploy via ZIP Deploy
```

### 3. Update Function App
```bash
cd FuncCosmicWwicUat
dotnet publish -c Release -o ./publish
# Deploy via ZIP Deploy
```

### 4. Configure Environment Variables
In Azure Portal → Function App → Configuration:
- Add `BLOB_STORAGE_ACCOUNT`
- Add `BLOB_CONTAINER_NAME`
- Optionally add `BLOB_MANAGED_IDENTITY_CLIENT_ID`
- Save and restart

### 5. Test Health Check
```bash
curl https://<function-app>.azurewebsites.net/api/health
```

Should include: `"mcpServer": "healthy"`

## 📈 Future Enhancements

- [ ] Support for PDF documents (OCR)
- [ ] Document summarization caching
- [ ] Process visualization/diagram extraction
- [ ] Multi-language support
- [ ] Real-time process discovery
- [ ] Performance metrics extraction
- [ ] Compliance requirement extraction

## 🔗 Related Components

- **MCP Server**: Blob Storage tools (blob_health_check, list_blobs_in_container, download_blob)
- **Azure OpenAI**: LLM for process analysis and formatting
- **Azure Blob Storage**: Document storage and retrieval
- **Azure Identity**: UAMI authentication

## 📞 Support

For issues with:
- **Intent Detection** → Check IntentService.cs and intent prompt
- **Blob Storage** → Check MCP Server blob tools and UAMI permissions
- **LLM Analysis** → Check Azure OpenAI configuration and token usage
- **Function Routing** → Check AgentFunction.cs routing logic
