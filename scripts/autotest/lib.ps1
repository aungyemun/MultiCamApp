# Shared harness helper: run a child process with a hard timeout (kills the whole process tree on expiry).
# ASCII-only on purpose (Windows PowerShell 5.1 reads BOM-less files as ANSI).
function Invoke-Timed {
  param([Parameter(Mandatory)][string]$File, [object[]]$ArgList = @(), [int]$TimeoutSec = 60)
  $q = { param($a) $s = "$a"; if ($s -eq '' -or $s -match '[\s"]') { '"' + ($s -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"' } else { $s } }
  $psi = New-Object Diagnostics.ProcessStartInfo
  $psi.FileName = $File
  $psi.Arguments = ($ArgList | ForEach-Object { & $q $_ }) -join ' '
  $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
  $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.RedirectStandardInput = $true
  $psi.StandardOutputEncoding = [Text.Encoding]::UTF8; $psi.StandardErrorEncoding = [Text.Encoding]::UTF8   # children emit UTF-8 (see mcui.ps1)
  $p = [Diagnostics.Process]::Start($psi)
  $p.StandardInput.Close()                       # child can never block waiting for stdin
  $so = $p.StandardOutput.ReadToEndAsync(); $se = $p.StandardError.ReadToEndAsync()
  if (-not $p.WaitForExit($TimeoutSec * 1000)) {
    & taskkill.exe /T /F /PID $p.Id 2>&1 | Out-Null
    $p.WaitForExit(5000) | Out-Null
    return [pscustomobject]@{ ExitCode = -9; TimedOut = $true; Out = ''; Err = "TIMEOUT after ${TimeoutSec}s: $File $($psi.Arguments)" }
  }
  $p.WaitForExit()                               # flush async readers
  [pscustomobject]@{ ExitCode = $p.ExitCode; TimedOut = $false; Out = $so.Result; Err = $se.Result }
}

# Repository-relative locations (no user- or machine-specific paths). Each can be overridden by an environment variable.
$McRoot   = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$McExe    = if ($env:MCA_EXE) { $env:MCA_EXE } else { Join-Path $McRoot 'source\MultiCamApp\MultiCamApp\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\MultiCamApp.exe' }
$McPy     = if ($env:MCA_PYTHON) { $env:MCA_PYTHON } else { Join-Path $McRoot 'tools\python\python.exe' }
$McFfDir  = if ($env:MCA_FFMPEG_DIR) { $env:MCA_FFMPEG_DIR } else { Join-Path $McRoot 'runtime\ffmpeg' }
$McRuns   = if ($env:MCA_RUNS_DIR) { $env:MCA_RUNS_DIR } else { Join-Path $McRoot 'data\temp\autotest\runs' }   # gitignored
$McVideos = if ($env:MCA_VIDEOS_DIR) { $env:MCA_VIDEOS_DIR } else { [Environment]::GetFolderPath('MyVideos') }   # app default output folder
New-Item -ItemType Directory $McRuns -Force | Out-Null
$env:MCA_FFMPEG_DIR = $McFfDir; $env:MCA_RUNS_DIR = $McRuns   # inherited by the Python tools
