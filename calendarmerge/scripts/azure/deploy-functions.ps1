param(
  [string]$SubscriptionId = $env:AZ_SUBSCRIPTION_ID,
  [string]$ResourceGroup = $env:AZ_RESOURCE_GROUP,
  [string]$FunctionAppName = $env:AZ_FUNCTIONAPP_NAME,
  [string]$OutputZip = ".artifacts/calendarmerge-functions.zip"
)

$ErrorActionPreference = "Stop"

function Assert-Required([string]$Value, [string]$Name) {
  if ([string]::IsNullOrWhiteSpace($Value)) {
    throw "$Name is required."
  }
}

Assert-Required $ResourceGroup "AZ_RESOURCE_GROUP"
Assert-Required $FunctionAppName "AZ_FUNCTIONAPP_NAME"

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$packageScript = Join-Path $PSScriptRoot "package-functions.ps1"

if ($SubscriptionId) {
  & az account set --subscription $SubscriptionId | Out-Null
}

$packageOutput = & $packageScript -OutputZip $OutputZip
$zipPath = ($packageOutput | Select-Object -Last 1).ToString().Trim()

if ([string]::IsNullOrWhiteSpace($zipPath) -or -not (Test-Path $zipPath)) {
  throw "Unable to resolve the deployment zip path from package-functions.ps1."
}

Push-Location $projectRoot
try {
  & az functionapp deployment source config-zip `
    --resource-group $ResourceGroup `
    --name $FunctionAppName `
    --src $zipPath `
    --build-remote false `
    --output none

  $storageAccount = & az functionapp config appsettings list `
    --resource-group $ResourceGroup `
    --name $FunctionAppName `
    --query "[?name=='OUTPUT_STORAGE_ACCOUNT'].value | [0]" `
    --output tsv

  if ([string]::IsNullOrWhiteSpace($storageAccount)) {
    throw "Unable to resolve OUTPUT_STORAGE_ACCOUNT from Function App settings."
  }

  $storageKey = & az storage account keys list `
    --resource-group $ResourceGroup `
    --account-name $storageAccount `
    --query "[0].value" `
    --output tsv

  if ([string]::IsNullOrWhiteSpace($storageKey)) {
    throw "Unable to resolve a storage account key for $storageAccount."
  }

  & az storage blob upload `
    --account-name $storageAccount `
    --account-key $storageKey `
    --container-name '$web' `
    --name index.html `
    --file (Join-Path $projectRoot "public/index.html") `
    --overwrite true `
    --content-type "text/html; charset=utf-8" `
    --only-show-errors `
    --output none
} finally {
  Pop-Location
}

Write-Host "Deployed package: $zipPath"
Write-Host "Status endpoint: https://$FunctionAppName.azurewebsites.net/api/status"
