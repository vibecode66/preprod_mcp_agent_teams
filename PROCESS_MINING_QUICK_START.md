# Process Mining Integration - Quick Start

## What Was Added

✅ **Intent Detection** - System now recognizes "process mining" queries
✅ **Blob Storage Integration** - Routes to MCP Server's Blob Storage tools
✅ **Document Search** - Smart keyword matching for relevant documents
✅ **LLM Analysis** - Uses Azure OpenAI to extract process information
✅ **Response Formatting** - Natural language formatted answers

## Quick Setup (5 mins)

### Step 1: Deploy Updated Services
```bash
# Deploy MCP Server with Blob Storage support
cd MCPServer
dotnet publish -c Release -o ./publish
# Use the deployment script from DEPLOYMENT_STEPS.md

# Deploy Function App
cd ../FuncCosmicWwicUat
dotnet publish -c Release -o ./publish
# Use the deployment script
```

### Step 2: Configure Environment
Add to Azure Function App Configuration:
```
BLOB_STORAGE_ACCOUNT = your-storage-account-name
BLOB_CONTAINER_NAME = process-mining
```

### Step 3: Upload Sample Documents
Upload a text file to `process-mining` container:
```
order-fulfillment-process.txt (or .json, .md)
```

Sample content:
```
Order Fulfillment Process:
1. Order Received - Customer submits order through portal
2. Validation - Order details are validated against inventory
3. Payment - Payment is processed and confirmed
4. Picking - Items are picked from warehouse
5. Packing - Items are packed for shipment
6. Shipping - Order is handed to carrier
7. Delivery - Customer receives order
```

### Step 4: Test
```bash
curl -X POST https://<function-app>.azurewebsites.net/api/agent \
  -H "Content-Type: application/json" \
  -d '{"message":"What is the process mining for order fulfillment?"}'
```

## Example Queries That Work

✅ "Show me the process mining analysis"
✅ "What is the workflow for order processing?"
✅ "Analyze the process flow for claims handling"
✅ "What is the process mining for customer onboarding?"
✅ "Show me the process discovery for payment processing"

## Response Format

```json
{
  "status": "success",
  "intent": "PROCESS_MINING",
  "reply": "The order fulfillment process includes 7 main steps: 1. Order Received...",
  "rows": [
    {
      "name": "order-fulfillment-process.txt",
      "size": 2048,
      "modified": "2026-01-15T10:30:00Z"
    }
  ]
}
```

## Architecture Flow

```
Query: "Process mining for order fulfillment?"
    ↓
Intent: PROCESS_MINING ✓
    ↓
Search Blob Storage (process-mining container)
    ↓
Find: order-fulfillment-process.txt ✓
    ↓
Download & Extract with LLM
    ↓
Format Response
    ↓
Return: "The order fulfillment process..."
```

## Key Files Modified

| File | Change |
|------|--------|
| `IntentService.cs` | Added PROCESS_MINING intent recognition |
| `McpClientService.cs` | Added ListBlobsAsync, DownloadBlobAsStringAsync |
| `AgentFunction.cs` | Added HandleProcessMiningAsync routing |
| `ProcessMiningService.cs` | **NEW** - Handles process mining logic |
| `AgentModels.cs` | Added BlobDocument, BlobListResponse models |
| `Program.cs` | Registered ProcessMiningService |

## Permissions Needed

✅ UAMI (uami-wwic-uat) must have **Storage Blob Data Reader** role on blob storage account

## Troubleshooting

| Error | Fix |
|-------|-----|
| Intent stays GENERAL | Keywords may not match (try: "process mining", "workflow", "process flow") |
| No documents found | Upload .txt or .json files to `process-mining` container |
| 403 Forbidden | Grant UAMI Storage Blob Data Reader role |
| Timeout | Increase request timeout or reduce document size |

## Next Steps

1. ✅ Deploy both services
2. ✅ Configure environment variables
3. ✅ Upload process documents to blob storage
4. ✅ Test with sample queries
5. ✅ Monitor logs for any issues

## Support

- Full documentation: **PROCESS_MINING_IMPLEMENTATION.md**
- Deployment steps: **DEPLOYMENT_STEPS.md**
- Blob storage setup: **BLOB_STORAGE_SETUP.md**
