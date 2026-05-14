$ErrorActionPreference = "Stop"

# ── paths ────────────────────────────────────────────────────────────
$peProj   = "src/PacketEngine/PacketEngine.csproj"
$appProj  = "src/A2Meter/A2Meter.csproj"
$peDst    = "src/A2Meter/Native/PacketEngine.dll"

[xml]$csproj = Get-Content $appProj
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$outDir  = "E:\A2Viewer\A2Meter\publish\$version"

# ── ensure vswhere is on PATH (NativeAOT linker needs it) ───────────
$vsInstallerPath = "C:\Program Files (x86)\Microsoft Visual Studio\Installer"
if ($env:PATH -notlike "*$vsInstallerPath*") {
    $env:PATH = "$vsInstallerPath;$env:PATH"
}

# ── 1. PacketEngine NativeAOT ────────────────────────────────────────
Write-Host "[1/3] Publishing PacketEngine (NativeAOT)..." -ForegroundColor Cyan
dotnet publish $peProj -c Release
if ($LASTEXITCODE -ne 0) { Write-Error "PacketEngine publish failed"; exit 1 }

$peSrc = "src/PacketEngine/bin/Release/net8.0/win-x64/publish/PacketEngine.dll"
Copy-Item $peSrc $peDst -Force
Write-Host "  -> Copied to $peDst" -ForegroundColor DarkGray

# ── 2. A2Meter build ────────────────────────────────────────────────
Write-Host "[2/3] Building A2Meter v$version..." -ForegroundColor Cyan
dotnet build $appProj -c Release -r win-x64 --no-restore
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

# ── 3. A2Meter publish ──────────────────────────────────────────────
Write-Host "[3/3] Publishing A2Meter -> $outDir" -ForegroundColor Cyan
dotnet publish $appProj -c Release -r win-x64 --self-contained false -o $outDir
if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed"; exit 1 }

Write-Host "Done: $outDir" -ForegroundColor Green
& "$outDir\A2Meter.exe"
