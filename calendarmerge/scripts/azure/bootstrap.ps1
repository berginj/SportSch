param(
  [string]$SubscriptionId = $env:AZ_SUBSCRIPTION_ID,
  [string]$Location = $env:AZ_LOCATION,
  [string]$ResourceGroup = $env:AZ_RESOURCE_GROUP,
  [string]$StorageAccount = $env:AZ_STORAGE_ACCOUNT,
  [string]$FunctionAppName = $env:AZ_FUNCTIONAPP_NAME,
  [string]$AppInsightsName = $env:AZ_APPINSIGHTS_NAME,
  [string]$SourceFeedsJson = $(if ($env:SOURCE_FEEDS_JSON) { $env:SOURCE_FEEDS_JSON } else { '[]' }),
  [string]$OutputContainer = $(if ($env:OUTPUT_CONTAINER) { $env:OUTPUT_CONTAINER } else { '$web' }),
  [string]$OutputBlobPath = $(if ($env:OUTPUT_BLOB_PATH) { $env:OUTPUT_BLOB_PATH } else { 'calendar.ics' }),
  [string]$PublicCalendarBlobPath = $(if ($env:PUBLIC_CALENDAR_BLOB_PATH) { $env:PUBLIC_CALENDAR_BLOB_PATH } else { 'calendar-public.ics' }),
  [string]$PublicGamesCalendarBlobPath = $(if ($env:PUBLIC_GAMES_CALENDAR_BLOB_PATH) { $env:PUBLIC_GAMES_CALENDAR_BLOB_PATH } else { 'calendar-games.ics' }),
  [string]$ScheduleXFullBlobPath = $(if ($env:SCHEDULE_X_FULL_BLOB_PATH) { $env:SCHEDULE_X_FULL_BLOB_PATH } else { 'schedule-x-full.json' }),
  [string]$ScheduleXGamesBlobPath = $(if ($env:SCHEDULE_X_GAMES_BLOB_PATH) { $env:SCHEDULE_X_GAMES_BLOB_PATH } else { 'schedule-x-games.json' }),
  [string]$StatusBlobPath = $(if ($env:STATUS_BLOB_PATH) { $env:STATUS_BLOB_PATH } else { 'status.json' }),
  [string]$OperatorStatusContainer = $(if ($env:OPERATOR_STATUS_CONTAINER) { $env:OPERATOR_STATUS_CONTAINER } else { 'sources' }),
  [string]$OperatorStatusBlobPath = $(if ($env:OPERATOR_STATUS_BLOB_PATH) { $env:OPERATOR_STATUS_BLOB_PATH } else { '_system/status-detail.json' }),
  [string]$UploadedSourcesContainer = $(if ($env:UPLOADED_SOURCES_CONTAINER) { $env:UPLOADED_SOURCES_CONTAINER } else { 'sources' }),
  [string]$UploadedSourcesPrefix = $(if ($env:UPLOADED_SOURCES_PREFIX) { $env:UPLOADED_SOURCES_PREFIX } else { 'uploads' }),
  [string]$RefreshLockContainer = $(if ($env:REFRESH_LOCK_CONTAINER) { $env:REFRESH_LOCK_CONTAINER } else { 'sources' }),
  [string]$RefreshLockBlobPath = $(if ($env:REFRESH_LOCK_BLOB_PATH) { $env:REFRESH_LOCK_BLOB_PATH } else { '_system/refresh.lock' }),
  [string]$RefreshSchedule = $(if ($env:REFRESH_SCHEDULE) { $env:REFRESH_SCHEDULE } else { '0 */15 * * * *' }),
  [string]$FetchTimeoutMs = $(if ($env:FETCH_TIMEOUT_MS) { $env:FETCH_TIMEOUT_MS } else { '10000' }),
  [string]$FetchRetryCount = $(if ($env:FETCH_RETRY_COUNT) { $env:FETCH_RETRY_COUNT } else { '2' }),
  [string]$FetchRetryDelayMs = $(if ($env:FETCH_RETRY_DELAY_MS) { $env:FETCH_RETRY_DELAY_MS } else { '750' }),
  [string]$MaxUploadBytes = $(if ($env:MAX_UPLOAD_BYTES) { $env:MAX_UPLOAD_BYTES } else { '5242880' }),
  [string]$MaxFetchBytes = $(if ($env:MAX_FETCH_BYTES) { $env:MAX_FETCH_BYTES } else { '5242880' })
)

$ErrorActionPreference = "Stop"

function Assert-Required([string]$Value, [string]$Name) {
  if ([string]::IsNullOrWhiteSpace($Value)) {
    throw "$Name is required."
  }
}

function Invoke-AzCli {
  param(
    [switch]$IgnoreErrors,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
  )

  $quotedArgs = $Arguments | ForEach-Object {
    if ($_ -match '[\s"&<>|^]') {
      '"' + ($_ -replace '"', '\"') + '"'
    } else {
      $_
    }
  }

  $commandLine = "az.cmd $($quotedArgs -join ' ') 2>&1"
  $output = & cmd.exe /d /c $commandLine
  $exitCode = $LASTEXITCODE

  if (-not $IgnoreErrors -and $exitCode -ne 0) {
    if ($output) {
      (($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine).Trim() | Write-Host
    }

    throw "az $($Arguments -join ' ') failed with exit code $exitCode."
  }

  if ($IgnoreErrors -and $exitCode -ne 0) {
    return ""
  }

  return (($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine).Trim()
}

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$deploymentName = "calendarmerge-bootstrap"

Assert-Required $Location "AZ_LOCATION"
Assert-Required $ResourceGroup "AZ_RESOURCE_GROUP"
Assert-Required $StorageAccount "AZ_STORAGE_ACCOUNT"
Assert-Required $FunctionAppName "AZ_FUNCTIONAPP_NAME"
Assert-Required $AppInsightsName "AZ_APPINSIGHTS_NAME"

if ($SubscriptionId) {
  Invoke-AzCli account set --subscription $SubscriptionId | Out-Null
}

Invoke-AzCli account show --output none | Out-Null

Invoke-AzCli group create --name $ResourceGroup --location $Location --output none | Out-Null

Invoke-AzCli deployment group create `
  --resource-group $ResourceGroup `
  --name $deploymentName `
  --template-file (Join-Path $projectRoot "infra/main.bicep") `
  --parameters location=$Location storageAccountName=$StorageAccount appInsightsName=$AppInsightsName `
  --output none | Out-Null

$existingFunctionName = Invoke-AzCli -IgnoreErrors functionapp show `
  --resource-group $ResourceGroup `
  --name $FunctionAppName `
  --query name `
  --output tsv

$functionExists = -not [string]::IsNullOrWhiteSpace($existingFunctionName)

if (-not $functionExists) {
  Invoke-AzCli functionapp create `
    --resource-group $ResourceGroup `
    --name $FunctionAppName `
    --storage-account $StorageAccount `
    --consumption-plan-location $Location `
    --os-type Windows `
    --functions-version 4 `
    --runtime node `
    --runtime-version 20 `
    --app-insights $AppInsightsName `
    --assign-identity "[system]" `
    --output none | Out-Null

  $createdFunctionName = Invoke-AzCli -IgnoreErrors functionapp show `
    --resource-group $ResourceGroup `
    --name $FunctionAppName `
    --query name `
    --output tsv

  if ([string]::IsNullOrWhiteSpace($createdFunctionName)) {
    throw "Function App creation did not succeed for $FunctionAppName."
  }
}

$settingsFile = [System.IO.Path]::GetTempFileName()
$settings = @{
  SERVICE_NAME = "calendarmerge"
  SOURCE_FEEDS_JSON = $SourceFeedsJson
  OUTPUT_STORAGE_ACCOUNT = $StorageAccount
  OUTPUT_CONTAINER = $OutputContainer
  OUTPUT_BLOB_PATH = $OutputBlobPath
  PUBLIC_CALENDAR_BLOB_PATH = $PublicCalendarBlobPath
  PUBLIC_GAMES_CALENDAR_BLOB_PATH = $PublicGamesCalendarBlobPath
  SCHEDULE_X_FULL_BLOB_PATH = $ScheduleXFullBlobPath
  SCHEDULE_X_GAMES_BLOB_PATH = $ScheduleXGamesBlobPath
  STATUS_BLOB_PATH = $StatusBlobPath
  OPERATOR_STATUS_CONTAINER = $OperatorStatusContainer
  OPERATOR_STATUS_BLOB_PATH = $OperatorStatusBlobPath
  UPLOADED_SOURCES_CONTAINER = $UploadedSourcesContainer
  UPLOADED_SOURCES_PREFIX = $UploadedSourcesPrefix
  REFRESH_LOCK_CONTAINER = $RefreshLockContainer
  REFRESH_LOCK_BLOB_PATH = $RefreshLockBlobPath
  REFRESH_SCHEDULE = $RefreshSchedule
  FETCH_TIMEOUT_MS = $FetchTimeoutMs
  FETCH_RETRY_COUNT = $FetchRetryCount
  FETCH_RETRY_DELAY_MS = $FetchRetryDelayMs
  MAX_UPLOAD_BYTES = $MaxUploadBytes
  MAX_FETCH_BYTES = $MaxFetchBytes
  WEBSITE_RUN_FROM_PACKAGE = "1"
}

try {
  $settings | ConvertTo-Json -Depth 4 | Set-Content -Path $settingsFile -Encoding utf8

  Invoke-AzCli functionapp config appsettings set `
    --resource-group $ResourceGroup `
    --name $FunctionAppName `
    --settings "@$settingsFile" `
    --output none | Out-Null
} finally {
  Remove-Item $settingsFile -Force -ErrorAction SilentlyContinue
}

$principalId = ""
for ($attempt = 0; $attempt -lt 12 -and [string]::IsNullOrWhiteSpace($principalId); $attempt += 1) {
  if ($attempt -gt 0) {
    Start-Sleep -Seconds 5
  }

  $principalId = Invoke-AzCli -IgnoreErrors functionapp identity show `
    --resource-group $ResourceGroup `
    --name $FunctionAppName `
    --query principalId `
    --output tsv
}

if ([string]::IsNullOrWhiteSpace($principalId)) {
  throw "Unable to resolve the Function App managed identity principal ID for $FunctionAppName."
}

$storageAccountId = Invoke-AzCli storage account show `
  --resource-group $ResourceGroup `
  --name $StorageAccount `
  --query id `
  --output tsv

$existingRoleAssignment = Invoke-AzCli -IgnoreErrors role assignment list `
  --assignee-object-id $principalId `
  --scope $storageAccountId `
  --role "Storage Blob Data Contributor" `
  --query "[0].id" `
  --output tsv

if (-not $existingRoleAssignment) {
  Invoke-AzCli role assignment create `
    --assignee-object-id $principalId `
    --assignee-principal-type ServicePrincipal `
    --scope $storageAccountId `
    --role "Storage Blob Data Contributor" `
    --output none | Out-Null
}

$storageAccountKey = Invoke-AzCli storage account keys list `
  --resource-group $ResourceGroup `
  --account-name $StorageAccount `
  --query "[0].value" `
  --output tsv

Invoke-AzCli storage blob service-properties update `
  --account-name $StorageAccount `
  --account-key $storageAccountKey `
  --static-website true `
  --index-document index.html `
  --404-document index.html `
  --output none | Out-Null

Invoke-AzCli storage blob upload `
  --account-name $StorageAccount `
  --account-key $storageAccountKey `
  --container-name '$web' `
  --name index.html `
  --file (Join-Path $projectRoot "public/index.html") `
  --overwrite true `
  --content-type "text/html; charset=utf-8" `
  --only-show-errors `
  --output none | Out-Null

$webEndpoint = Invoke-AzCli storage account show `
  --resource-group $ResourceGroup `
  --name $StorageAccount `
  --query "primaryEndpoints.web" `
  --output tsv

Write-Host "Provisioned resource group: $ResourceGroup"
Write-Host "Function App: $FunctionAppName"
Write-Host "Status endpoint: https://$FunctionAppName.azurewebsites.net/api/status"
Write-Host "Static website endpoint: $webEndpoint"
Write-Host "Public ICS URL: $($webEndpoint.TrimEnd('/'))/$OutputBlobPath"
Write-Host "Public sanitized ICS URL: $($webEndpoint.TrimEnd('/'))/$PublicCalendarBlobPath"
Write-Host "Public games ICS URL: $($webEndpoint.TrimEnd('/'))/$PublicGamesCalendarBlobPath"
Write-Host "Schedule-X full feed URL: $($webEndpoint.TrimEnd('/'))/$ScheduleXFullBlobPath"
Write-Host "Schedule-X games feed URL: $($webEndpoint.TrimEnd('/'))/$ScheduleXGamesBlobPath"
