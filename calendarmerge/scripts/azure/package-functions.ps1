param(
  [string]$OutputZip = ".artifacts/calendarmerge-functions.zip"
)

$ErrorActionPreference = "Stop"

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$artifactRoot = Join-Path $projectRoot ".artifacts"
$stagingRoot = Join-Path $artifactRoot "package"
$stagingAppRoot = Join-Path $stagingRoot "app"
$resolvedZip = if ([System.IO.Path]::IsPathRooted($OutputZip)) {
  $OutputZip
} else {
  Join-Path $projectRoot $OutputZip
}

Push-Location $projectRoot
try {
  if (Test-Path $stagingRoot) {
    Remove-Item $stagingRoot -Recurse -Force
  }

  New-Item -ItemType Directory -Path $stagingAppRoot -Force | Out-Null

  & npm run build

  Copy-Item package.json, package-lock.json, host.json -Destination $stagingAppRoot
  Copy-Item dist -Destination $stagingAppRoot -Recurse

  Push-Location $stagingAppRoot
  try {
    & npm ci --omit=dev --ignore-scripts --no-fund

    $zipDirectory = Split-Path -Parent $resolvedZip
    if (-not (Test-Path $zipDirectory)) {
      New-Item -ItemType Directory -Path $zipDirectory -Force | Out-Null
    }

    if (Test-Path $resolvedZip) {
      Remove-Item $resolvedZip -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stagingAppRoot, $resolvedZip)
  } finally {
    Pop-Location
  }
} finally {
  Pop-Location
}

Write-Output $resolvedZip
