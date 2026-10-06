param([string]$Version = '1.0.1', [string]$Dotnet = 'dotnet', [string]$Iscc = '')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRoot
& $Dotnet build ReplayKit.sln -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
& $Dotnet run --project tests/ReplayKit.Tests -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
& $Dotnet run --project tests/ReplayKit.WindowsTests -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Windows tests failed' }
& $Dotnet publish src/ReplayKit/ReplayKit.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" -o artifacts/publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Compress-Archive -Path artifacts/publish/* -DestinationPath "artifacts/ReplayKit-$Version-win-x64.zip" -Force
if (!$Iscc) {
    $taskCompiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($taskCompiler) { $Iscc = $taskCompiler.Source }
    elseif (Test-Path 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe') { $Iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' }
}
if ($Iscc) {
    & $Iscc "/DAppVersion=$Version" packaging/ReplayKit.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
} else { Write-Warning 'Inno Setup compiler unavailable: portable ZIP built; CI builds the installer.' }
Get-ChildItem artifacts/*.zip, artifacts/*Setup*.exe -ErrorAction SilentlyContinue | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))" } | Set-Content artifacts/SHA256SUMS.txt -Encoding utf8
