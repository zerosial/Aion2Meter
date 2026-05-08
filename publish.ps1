$proj = "src/A2Meter/A2Meter.csproj"
[xml]$csproj = Get-Content $proj
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { Write-Error "Version not found in csproj"; exit 1 }

# 빌드 출력 및 압축 경로 설정 (로컬 프로젝트 내부 상대 경로)
$publishDir = "publish"
$outDir = "$publishDir\A2Meter-v$version"
$zipPath = "$publishDir\A2Meter-v$version-win-x64.zip"

Write-Host "Publishing v$version -> $outDir" -ForegroundColor Cyan

# 1. 이전 빌드 및 압축 파일 정리
if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

# 2. dotnet publish 실행 (--self-contained false 로 설정하면 닷넷 런타임 제외, true로 변경시 런타임 포함)
dotnet publish $proj -c Release -r win-x64 --self-contained false -o $outDir
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed"
    exit 1
}

# 3. ZIP 파일로 압축
Write-Host "Compressing to $zipPath..." -ForegroundColor Cyan
Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath -Force
Write-Host "Zip compression finished: $zipPath" -ForegroundColor Green

# 4. GitHub CLI (gh)가 설치 및 로그인되어 있는지 확인 후 업로드
if (Get-Command gh -ErrorAction SilentlyContinue) {
    Write-Host "GitHub Release 생성 및 업로드 진행 중..." -ForegroundColor Cyan
    # 안전을 위해 Draft(초안) 상태로 릴리즈를 생성합니다. (원할 경우 --draft 옵션을 제거하여 바로 배포할 수 있습니다.)
    gh release create "v$version" $zipPath --title "v$version" --notes "A2Meter Release v$version" --draft
    if ($LASTEXITCODE -eq 0) {
        Write-Host "GitHub Release 드래프트가 성공적으로 생성 및 업로드되었습니다! GitHub 웹사이트에서 확인 및 배포를 마무리해 주세요." -ForegroundColor Green
    } else {
        Write-Warning "GitHub Release 생성에 실패했습니다. gh CLI 로그인 상태를 확인해 주세요. (명령어: gh auth login)"
    }
} else {
    Write-Warning "GitHub CLI(gh)가 설치되어 있지 않아 자동 업로드를 건너뜁니다."
    Write-Host "팁: 'winget install --id GitHub.cli' 로 설치 후 'gh auth login'을 실행해 주세요." -ForegroundColor Yellow
}

