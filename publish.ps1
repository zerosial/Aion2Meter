$proj = "src/A2Meter/A2Meter.csproj"
[xml]$csproj = Get-Content $proj
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { Write-Error "Version not found in csproj"; exit 1 }

$outDir = "E:\A2Viewer\A2Meter\publish\$version"
Write-Host "Publishing v$version -> $outDir" -ForegroundColor Cyan

dotnet publish $proj -c Release -r win-x64 --self-contained false -o $outDir
if ($LASTEXITCODE -eq 0) {
    Write-Host "Done: $outDir" -ForegroundColor Green
} else {
    Write-Error "Publish failed"
    exit 1
}
