dotnet build src/A2Meter/A2Meter.csproj -c Release -r win-x64 --no-restore
if ($LASTEXITCODE -eq 0) {
    & "src\A2Meter\bin\Release\net8.0-windows\win-x64\A2Meter.exe"
}
