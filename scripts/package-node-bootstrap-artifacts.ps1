param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = ".\artifacts\node-bootstrap"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$outputRootPath = Join-Path $repoRoot $OutputRoot
$apiPublish = Join-Path $outputRootPath "Egs.Api"
$agentPublish = Join-Path $outputRootPath "Egs.Agent.Windows"

New-Item -ItemType Directory -Force -Path $outputRootPath | Out-Null
Remove-Item -Recurse -Force -Path $apiPublish -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force -Path $agentPublish -ErrorAction SilentlyContinue

dotnet publish (Join-Path $repoRoot "src\Egs.Api\Egs.Api.csproj") `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained false `
    --output $apiPublish

dotnet publish (Join-Path $repoRoot "src\Egs.Agent.Windows\Egs.Agent.Windows.csproj") `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained false `
    --output $agentPublish

$apiZip = Join-Path $outputRootPath "Egs.Api-$Runtime.zip"
$agentZip = Join-Path $outputRootPath "Egs.Agent.Windows-$Runtime.zip"

Remove-Item -Force -Path $apiZip -ErrorAction SilentlyContinue
Remove-Item -Force -Path $agentZip -ErrorAction SilentlyContinue

Compress-Archive -Path (Join-Path $apiPublish "*") -DestinationPath $apiZip -Force
Compress-Archive -Path (Join-Path $agentPublish "*") -DestinationPath $agentZip -Force

Write-Host "Created $apiZip"
Write-Host "Created $agentZip"
Write-Host "Upload these zip files to GitHub Releases or Azure Blob Storage, then paste the URLs into the website Deploy Node form."
