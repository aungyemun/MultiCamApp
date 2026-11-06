# Installer test that never touches the installed MultiCamApp: builds test variants of the installer
# (different AppId and AppName, same dist payload), installs into folders under data\temp\installtest,
# and uninstalls them again. Scenario A upgrades a COPY of the installed 2.0.5 folder.
# Aborts if any MultiCamApp.exe is running (Setup closes every process with that name).
param([string]$Iscc = '')
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"
$T = Join-Path $McRoot 'data\temp\installtest'; New-Item -ItemType Directory $T -Force | Out-Null
$inst = Join-Path $McRoot 'installer'
if (-not $Iscc) { $Iscc = @("$env:ProgramFiles\Inno Setup 7\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", (Join-Path $McRoot 'tools\inno\ISCC.exe')) | ? { Test-Path $_ } | Select -First 1 }
$TestId = '{{C3E1F7A2-5B4D-4E8F-9A6C-1D2E3F4A5B6C}'; $TestName = 'MultiCamApp-ReleaseTest'
$RealKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A8C4E2B1-9F3D-4A6E-B5C1-2D8E7F0A3B4C}_is1'
$TestKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{C3E1F7A2-5B4D-4E8F-9A6C-1D2E3F4A5B6C}_is1'
function Guard { if (Get-Process MultiCamApp -ErrorAction SilentlyContinue) { throw 'MultiCamApp is running - aborting so Setup cannot close it' } }
function RealState { if (Test-Path $RealKey) { $p = Get-ItemProperty $RealKey; $loc = $p.InstallLocation
  "$($p.DisplayVersion)|$((Get-Item (Join-Path $loc 'MultiCamApp.exe')).LastWriteTimeUtc.Ticks)|$((Get-ChildItem $loc -Recurse -File | Measure-Object Length -Sum).Sum)" } else { 'absent' } }
function Variant($src, $name) {
  $s = [IO.File]::ReadAllText($src)
  $s = $s.Replace('AppId={{A8C4E2B1-9F3D-4A6E-B5C1-2D8E7F0A3B4C}', "AppId=$TestId").Replace('#define AppName "MultiCamApp"', "#define AppName `"$TestName`"")
  if (-not $s.Contains($TestId) -or -not $s.Contains($TestName)) { throw "variant substitution failed for $src" }
  $iss = Join-Path $inst "_rt_$name.iss"; [IO.File]::WriteAllText($iss, $s, (New-Object Text.UTF8Encoding $true))
  try { $o = & $Iscc "/O$T" "/F$name" '/DAppVersion=2.1.0' '/DPublishDir=..\dist' $iss 2>&1; if ($LASTEXITCODE) { throw "ISCC failed: $($o | Select -Last 5)" } } finally { Remove-Item $iss -Force }
  Join-Path $T "$name.exe" }
function Run($exe, [string[]]$a, $sec = 600) { $p = Start-Process $exe -ArgumentList $a -PassThru; if (-not $p.WaitForExit($sec * 1000)) { $p.Kill(); throw "$exe timed out" }; $p.ExitCode }
function Seed($dir) {  # user data that must survive upgrade and uninstall
  New-Item -ItemType Directory (Join-Path $dir 'user_settings'), (Join-Path $dir 'MyRecordings\s1\cam1') -Force | Out-Null
  Set-Content (Join-Path $dir 'user_settings\appsettings.user.json') '{"releaseTest":true}'
  [IO.File]::WriteAllBytes((Join-Path $dir 'MyRecordings\s1\cam1\cam1.mp4'), [byte[]](1..64))
  Set-Content (Join-Path $dir 'notes_from_user.txt') 'unrelated user file' }
function Hash($f) { (Get-FileHash $f -Algorithm SHA256).Hash }

$R = [ordered]@{ iscc = Split-Path $Iscc -Leaf; realBefore = RealState }
Guard
$fixed = Variant (Join-Path $inst 'MultiCamApp.iss') 'RT_fixed'
$prefix = Variant (Join-Path $T 'MultiCamApp_pre_fix.iss') 'RT_prefix'
$dist = Join-Path $McRoot 'dist'
$distFiles = Get-ChildItem $dist -Recurse -File | ? { $_.Name -notlike '*.pdb' } | % { $_.FullName.Substring($dist.Length + 1) }

# ---- A: upgrade a copy of the installed 2.0.5 folder, launch offline, uninstall ----
$A = Join-Path $T 'appA'; if (Test-Path $A) { Remove-Item $A -Recurse -Force }
$loc = (Get-ItemProperty $RealKey).InstallLocation
Copy-Item $loc $A -Recurse
$R.A_copiedFromVersion = (Get-ItemProperty $RealKey).DisplayVersion
$R.A_before = (Get-Item (Join-Path $A 'MultiCamApp.exe')).VersionInfo.ProductVersion
Seed $A; Set-Content (Join-Path $A 'logs\release_test_marker.log') 'marker'
Guard
$R.A_installExit = Run $fixed @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', "/DIR=`"$A`"", '/TASKS=""', "/LOG=`"$T\A_install.log`"")
$R.A_exeVersion = (Get-Item (Join-Path $A 'MultiCamApp.exe')).VersionInfo.ProductVersion
$R.A_versionJson = (Get-Content (Join-Path $A 'config\version.json') -Raw | ConvertFrom-Json) | Select version, build
$missing = @(); $differ = @(); foreach ($f in $distFiles) { $i = Join-Path $A $f; if (-not (Test-Path $i)) { $missing += $f } elseif ((Hash $i) -ne (Hash (Join-Path $dist $f))) { $differ += $f } }
$R.A_distFiles = $distFiles.Count; $R.A_missing = $missing; $R.A_differentFromDist = $differ
$R.A_userSettingsKept = (Get-Content (Join-Path $A 'user_settings\appsettings.user.json') -Raw -ErrorAction SilentlyContinue) -match 'releaseTest'
$R.A_logMarkerKept = Test-Path (Join-Path $A 'logs\release_test_marker.log')
$R.A_recordingKept = Test-Path (Join-Path $A 'MyRecordings\s1\cam1\cam1.mp4')
$R.A_backupMade = @(Get-ChildItem (Join-Path $A 'backup_before_update') -Recurse -File -ErrorAction SilentlyContinue).Count
$R.A_ffmpeg = ((& (Join-Path $A 'runtime\ffmpeg\ffmpeg.exe') -version 2>&1) | Select -First 1) -replace 'Copyright.*', ''
$R.A_ffprobe = ((& (Join-Path $A 'runtime\ffmpeg\ffprobe.exe') -version 2>&1) | Select -First 1) -replace 'Copyright.*', ''
$R.A_vcRuntimeBundled = Test-Path (Join-Path $A 'runtime\vc_runtime\vc_redist.x64.exe')
$R.A_runtimeSetupExit = (Get-Content (Join-Path $A 'logs\runtime_setup_exit.txt') -ErrorAction SilentlyContinue | Select -First 1)
$R.A_installLogTail = @(Get-Content (Join-Path $A 'logs\install_update.log') -ErrorAction SilentlyContinue | Select -Last 6)
$R.A_testKeyAfterInstall = Test-Path $TestKey
$rc = Get-Content (Join-Path $A 'MultiCamApp.runtimeconfig.json') -Raw -ErrorAction SilentlyContinue
$R.A_selfContained = -not $rc -or -not ($rc -match '"framework"')
# launch the upgraded copy, watch for network activity, close
Guard
$p = Start-Process (Join-Path $A 'MultiCamApp.exe') -WorkingDirectory $A -PassThru
$net = @(); $win = $false
for ($i = 0; $i -lt 25; $i++) { Start-Sleep 1; $p.Refresh(); if ($p.MainWindowHandle -ne 0) { $win = $true }
  $net += @(Get-NetTCPConnection -OwningProcess $p.Id -ErrorAction SilentlyContinue | % { "$($_.RemoteAddress):$($_.RemotePort) $($_.State)" })
  $net += @(Get-NetUDPEndpoint -OwningProcess $p.Id -ErrorAction SilentlyContinue | % { "udp $($_.LocalAddress):$($_.LocalPort)" }) }
$R.A_launchWindow = $win; $R.A_networkEndpoints = @($net | Select -Unique)
$p.CloseMainWindow() | Out-Null; if (-not $p.WaitForExit(30000)) { $p.Kill() }
Start-Sleep 2; Guard
$R.A_uninstallExit = Run (Join-Path $A 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$T\A_uninstall.log`"")
Start-Sleep 3
$R.A_afterUninstall_exe = Test-Path (Join-Path $A 'MultiCamApp.exe')
$R.A_afterUninstall_recording = Test-Path (Join-Path $A 'MyRecordings\s1\cam1\cam1.mp4')
$R.A_afterUninstall_userFile = Test-Path (Join-Path $A 'notes_from_user.txt')
$R.A_afterUninstall_left = @(Get-ChildItem $A -Recurse -File -ErrorAction SilentlyContinue | % { $_.FullName.Substring($A.Length + 1) })
$R.A_testKeyAfterUninstall = Test-Path $TestKey

# ---- B: same uninstall with the pre-fix installer (demonstrates the old behaviour) ----
foreach ($case in @(@{ n = 'B_prefix'; exe = $prefix }, @{ n = 'C_fixed_fresh'; exe = $fixed })) {
  $D = Join-Path $T ("app" + $case.n); if (Test-Path $D) { Remove-Item $D -Recurse -Force }
  Guard
  $R["$($case.n)_installExit"] = Run $case.exe @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', "/DIR=`"$D`"", '/TASKS=""', "/LOG=`"$T\$($case.n)_install.log`"")
  if ($case.n -eq 'B_prefix') { Seed $D }
  $R["$($case.n)_uninstallExit"] = Run (Join-Path $D 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
  Start-Sleep 3
  $R["$($case.n)_folderExists"] = Test-Path $D
  $R["$($case.n)_recordingKept"] = Test-Path (Join-Path $D 'MyRecordings\s1\cam1\cam1.mp4')
  $R["$($case.n)_leftFiles"] = @(Get-ChildItem $D -Recurse -File -ErrorAction SilentlyContinue).Count }
$R.testKeyAtEnd = Test-Path $TestKey
$R.startMenuTestGroup = Test-Path (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$TestName")
$R.desktopTestShortcut = Test-Path (Join-Path ([Environment]::GetFolderPath('Desktop')) "$TestName.lnk")
$R.realAfter = RealState
$R.realUnchanged = $R.realBefore -eq $R.realAfter
$out = Join-Path $McRuns 'installtest.json'
[IO.File]::WriteAllText($out, ($R | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding $false))
"wrote $out"
