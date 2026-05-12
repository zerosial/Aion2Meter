$proj = "src/A2Meter/A2Meter.csproj"
[xml]$csproj = Get-Content $proj
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { Write-Error "Version not found in csproj"; exit 1 }

# 빌드 출력 및 압축 경로 설정 (관리자용 특화 폴더 및 파일명)
$publishDir = "publish"
$outDir = "$publishDir\A2Meter-Admin-v$version"
$zipPath = "$publishDir\A2Meter-Admin-v$version-win-x64.zip"

Write-Host "Publishing Admin Edition v$version -> $outDir" -ForegroundColor Cyan

# 1. 이전 빌드 및 압축 파일 정리
if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

# 2. dotnet publish 실행
dotnet publish $proj -c Release -r win-x64 --self-contained false -o $outDir -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed"
    exit 1
}

# [추가] 압축 전 디버깅 기호 파일(*.pdb) 완전히 제거
Write-Host "Cleaning up debugging symbol (*.pdb) files..." -ForegroundColor Cyan
Remove-Item -Path "$outDir\*.pdb" -Force -ErrorAction SilentlyContinue

# [관리자 전용] 핵심 실행 파일명을 A2Meter_Admin.exe로 변경하여, 아규먼트 없이 더블클릭만 해도 관리자 모드가 자동 발동되도록 설정
Write-Host "Configuring Executable for Zero-Config Admin Mode..." -ForegroundColor Yellow
if (Test-Path "$outDir\A2Meter.exe") {
    Rename-Item -Path "$outDir\A2Meter.exe" -NewName "A2Meter_Admin.exe" -Force
    Write-Host "Successfully renamed A2Meter.exe -> A2Meter_Admin.exe" -ForegroundColor Green
} else {
    Write-Warning "A2Meter.exe not found in build output directory!"
}

# 3. ZIP 파일로 압축
Write-Host "Compressing to $zipPath..." -ForegroundColor Cyan
Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath -Force
Write-Host "Zip compression finished: $zipPath" -ForegroundColor Green

# 4. GitHub CLI (gh)가 설치 및 로그인되어 있는지 확인 후 업로드
if (Get-Command gh -ErrorAction SilentlyContinue) {
    Write-Host "GitHub Release (Admin Draft) 생성 및 업로드 진행 중..." -ForegroundColor Cyan
    gh release create "v$version-admin" $zipPath --title "v$version (Admin Edition)" --notes "A2Meter Admin Release v$version. Includes Auto-Boss-Registration-and-Database-Persistence." --draft
    if ($LASTEXITCODE -eq 0) {
        Write-Host "GitHub Admin Release 드래프트가 성공적으로 생성 및 업로드되었습니다!" -ForegroundColor Green
    } else {
        Write-Warning "GitHub Release 생성에 실패했습니다. gh CLI 로그인 상태를 확인해 주세요. (명령어: gh auth login)"
    }
} else {
    Write-Warning "GitHub CLI(gh)가 설치되어 있지 않아 자동 업로드를 건너뜁니다."
}
