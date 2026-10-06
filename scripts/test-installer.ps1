param([string]$SetupPath, [string]$InstallPath)
$ErrorActionPreference = 'Stop'
$taskSetupPath = (Resolve-Path -LiteralPath $SetupPath).Path
$taskInstallPath = [IO.Path]::GetFullPath($InstallPath)
$taskArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/MERGETASKS="!autostart,!desktopicon"', "/DIR=`"$taskInstallPath`"")
$taskInstaller = Start-Process -FilePath $taskSetupPath -ArgumentList $taskArguments -PassThru -Wait -WindowStyle Hidden
if ($taskInstaller.ExitCode -ne 0) { throw "Installation failed: $($taskInstaller.ExitCode)" }
$taskExe = Join-Path $taskInstallPath 'ReplayKit.exe'
if (!(Test-Path -LiteralPath $taskExe)) { throw 'Installed executable is missing' }
if (!(Test-Path -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{716F4DA6-7EA4-45C0-AE9A-6434053EA0B4}_is1')) { throw 'Per-user uninstall entry is missing' }
$taskApp = Start-Process -FilePath $taskExe -ArgumentList '--background' -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 3
if ($taskApp.HasExited) { throw 'Self-contained app exited on startup' }
$taskQuit = Start-Process -FilePath $taskExe -ArgumentList '--quit' -PassThru -Wait -WindowStyle Hidden
if (!$taskApp.WaitForExit(10000)) { throw 'App did not shut down cleanly' }
$taskUninstaller = Start-Process -FilePath (Join-Path $taskInstallPath 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -PassThru -Wait -WindowStyle Hidden
if ($taskUninstaller.ExitCode -ne 0 -or (Test-Path -LiteralPath $taskExe)) { throw 'Uninstall failed' }
Write-Output 'PASS per-user installation, self-contained startup, clean shutdown, and uninstall'
