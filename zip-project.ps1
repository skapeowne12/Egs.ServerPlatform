$root = (Get-Location).Path
$zip = Join-Path $root "Egs.ServerPlatform-upload.zip"
$temp = Join-Path $env:TEMP "Egs.ServerPlatform-upload"

if (Test-Path $zip) { Remove-Item $zip -Force }
if (Test-Path $temp) { Remove-Item $temp -Recurse -Force }

$excludePathPattern = '[\\/](bin|obj|\.vs|\.git|TestResults|packages|node_modules)[\\/]'
$excludeFilePattern = '\.(user|suo|db|db-shm|db-wal|tmp|cache)$'

Get-ChildItem -Path $root -Recurse -File |
    Where-Object {
        $_.FullName -ne $zip -and
        $_.FullName -notmatch $excludePathPattern -and
        $_.Name -notmatch $excludeFilePattern
    } |
    ForEach-Object {
        $relative = $_.FullName.Substring($root.Length).TrimStart('\')
        $dest = Join-Path $temp $relative
        $destDir = Split-Path $dest -Parent
        if (!(Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }
        Copy-Item $_.FullName $dest
    }

Compress-Archive -Path (Join-Path $temp '*') -DestinationPath $zip -Force
Remove-Item $temp -Recurse -Force
Write-Host "Created: $zip"