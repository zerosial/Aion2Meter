$proj = "src/A2Meter/A2Meter.csproj"
[xml]$csproj = Get-Content $proj
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { Write-Error "Version not found in csproj"; exit 1 }

$publishDir = "publish"
$outDir = "$publishDir\A2Meter-Admin-v$version"
$zipPath = "$publishDir\A2Meter-Admin-v$version-win-x64.zip"

Write-Host "Publishing Admin Edition v$version -> $outDir" -ForegroundColor Cyan

if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

dotnet publish $proj -c Release -r win-x64 --self-contained false -o $outDir -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed"
    exit 1
}

Write-Host "Cleaning up debugging symbol (*.pdb) files..." -ForegroundColor Cyan
Remove-Item -Path "$outDir\*.pdb" -Force -ErrorAction SilentlyContinue

Write-Host "Configuring Executable for Zero-Config Admin Mode..." -ForegroundColor Yellow
if (Test-Path "$outDir\A2Meter.exe") {
    Rename-Item -Path "$outDir\A2Meter.exe" -NewName "A2Meter_Admin.exe" -Force
    Write-Host "Successfully renamed A2Meter.exe -> A2Meter_Admin.exe" -ForegroundColor Green
} else {
    Write-Warning "A2Meter.exe not found in build output directory!"
}

Write-Host "Compressing to $zipPath..." -ForegroundColor Cyan
Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath -Force
Write-Host "Zip compression finished: $zipPath" -ForegroundColor Green
