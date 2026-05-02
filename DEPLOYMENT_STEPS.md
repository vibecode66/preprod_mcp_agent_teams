# MCP Server Deployment to Azure

## Overview
These are the proven steps to deploy the MCP Server to Azure App Service.

## Prerequisites
- Azure CLI installed and authenticated (`az login`)
- .NET 8.0 SDK installed
- App Service already created in Azure

## Deployment Steps

### Step 1: Clean Previous Builds
```powershell
cd "C:\Users\v-nadusumill\OneDrive - Microsoft\Documents\Copilot_MCP_Agent\MCPServer"

# Remove old build artifacts
Remove-Item -Recurse -Force obj     -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force bin     -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force publish -ErrorAction SilentlyContinue
```

### Step 2: Publish in Release Mode
```powershell
dotnet publish -c Release -o ./publish
```
This creates an optimized production build in the `publish` folder.

### Step 3: Create Deployment Package
```powershell
Compress-Archive -Path ".\publish\*" -DestinationPath "$env:TEMP\publish.zip" -Force
```
Packages the published files into a ZIP for deployment.

### Step 4: Get Azure Publishing Credentials
```powershell
$creds = az webapp deployment list-publishing-credentials `
  --resource-group rg-wwic-uat-eastus `
  --name app-cosmic-wwic-uat-eastus `
  --query "{user:publishingUserName, pass:publishingPassword}" `
  --output json | ConvertFrom-Json
```

**Parameters:**
- `--resource-group`: Your resource group name
- `--name`: Your App Service name

### Step 5: Create Authorization Header
```powershell
$base64 = [Convert]::ToBase64String(
  [Text.Encoding]::ASCII.GetBytes("$($creds.user):$($creds.pass)"))
echo $base64
```

### Step 6: Deploy via ZIP Deploy API
```powershell
Invoke-RestMethod `
  -Uri "https://app-cosmic-wwic-uat-eastus.scm.azurewebsites.net/api/zipdeploy" `
  -Method POST `
  -Headers @{ Authorization = "Basic $base64" } `
  -ContentType "application/zip" `
  -InFile "$env:TEMP\publish.zip" `
  -TimeoutSec 300
```

**Parameters:**
- Replace `app-cosmic-wwic-uat-eastus` with your App Service name
- TimeoutSec 300 allows 5 minutes for deployment

## Reusable Deployment Script

Save this as `Deploy-MCPServer.ps1`:

```powershell
param(
    [string]$ResourceGroup = "rg-wwic-uat-eastus",
    [string]$AppServiceName = "app-cosmic-wwic-uat-eastus",
    [string]$MCPServerPath = "C:\Users\v-nadusumill\OneDrive - Microsoft\Documents\Copilot_MCP_Agent\MCPServer"
)

Write-Host "🚀 Starting MCP Server deployment..." -ForegroundColor Green

# Step 1: Clean
Write-Host "📦 Cleaning previous builds..."
cd $MCPServerPath
Remove-Item -Recurse -Force obj     -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force bin     -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force publish -ErrorAction SilentlyContinue

# Step 2: Publish
Write-Host "🔨 Publishing Release build..."
dotnet publish -c Release -o ./publish
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Publish failed!" -ForegroundColor Red
    exit 1
}

# Step 3: Package
Write-Host "📦 Creating deployment package..."
Compress-Archive -Path ".\publish\*" -DestinationPath "$env:TEMP\publish.zip" -Force

# Step 4: Get credentials
Write-Host "🔐 Retrieving publishing credentials..."
$creds = az webapp deployment list-publishing-credentials `
  --resource-group $ResourceGroup `
  --name $AppServiceName `
  --query "{user:publishingUserName, pass:publishingPassword}" `
  --output json | ConvertFrom-Json

if (!$creds) {
    Write-Host "❌ Failed to get credentials!" -ForegroundColor Red
    exit 1
}

# Step 5: Encode
$base64 = [Convert]::ToBase64String(
  [Text.Encoding]::ASCII.GetBytes("$($creds.user):$($creds.pass)"))

# Step 6: Deploy
Write-Host "🚀 Deploying to $AppServiceName..."
$deployUri = "https://$AppServiceName.scm.azurewebsites.net/api/zipdeploy"

try {
    Invoke-RestMethod `
      -Uri $deployUri `
      -Method POST `
      -Headers @{ Authorization = "Basic $base64" } `
      -ContentType "application/zip" `
      -InFile "$env:TEMP\publish.zip" `
      -TimeoutSec 300 | Out-Null
    
    Write-Host "✅ Deployment successful!" -ForegroundColor Green
    Write-Host "📍 App URL: https://$AppServiceName.azurewebsites.net" -ForegroundColor Cyan
}
catch {
    Write-Host "❌ Deployment failed: $_" -ForegroundColor Red
    exit 1
}
```

**Usage:**
```powershell
.\Deploy-MCPServer.ps1 -ResourceGroup "rg-wwic-uat-eastus" -AppServiceName "app-cosmic-wwic-uat-eastus"
```

## Post-Deployment Verification

### 1. Check App Service Status
```powershell
az webapp show `
  --resource-group rg-wwic-uat-eastus `
  --name app-cosmic-wwic-uat-eastus `
  --query "state"
```

### 2. View Live Logs
```powershell
az webapp log tail `
  --resource-group rg-wwic-uat-eastus `
  --name app-cosmic-wwic-uat-eastus
```

### 3. Test Blob Storage Connection
```powershell
Invoke-RestMethod `
  -Uri "https://app-cosmic-wwic-uat-eastus.azurewebsites.net/mcp/tools/blob_health_check" `
  -Method Post `
  -ContentType "application/json"
```

## Environment Variables Configuration

After deployment, configure these in Azure App Service:

1. Go to **App Service** → **Configuration** → **Application settings**
2. Add:
   - `SQL_SERVER` - SQL Server name
   - `SQL_DATABASE` - Database name
   - `SQL_MANAGED_IDENTITY_CLIENT_ID` - UAMI Client ID
   - `BLOB_STORAGE_ACCOUNT` - Storage account name
   - `BLOB_MANAGED_IDENTITY_CLIENT_ID` - (Optional) UAMI Client ID for Blob

3. Click **Save** and the App Service will restart automatically

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "Publish failed" | Check .NET version: `dotnet --version` |
| "Failed to get credentials" | Run `az login` and verify resource group/app name |
| 403 Unauthorized on deploy | Publishing credentials may be expired, reset in App Service |
| App doesn't start after deploy | Check logs: `az webapp log tail --resource-group ... --name ...` |

## Deployment Time
Expected deployment time: **2-5 minutes**
