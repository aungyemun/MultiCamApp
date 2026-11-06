# Scans every file Git would commit (tracked + untracked, not ignored) for personal data, credentials,
# private paths and generated test output. Prints counts and redacted locations only.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"
Set-Location $McRoot
$files = @(git ls-files) + @(git ls-files --others --exclude-standard) | Sort-Object -Unique | ? { $_ -and (Test-Path $_ -PathType Leaf) }
$bin = '\.(png|ico|jpg|jpeg|gif|exe|dll|zip|pdf|mp4|ttf|otf|woff2?)$'
$checks = [ordered]@{
  userProfilePath  = '[A-Za-z]:\\Users\\(?!(YOU|someone|<name>|Public)\\)[^\\\s"]+'
  localRepoPath    = 'Software testing'
  email            = '[A-Za-z0-9._%+-]+@(?!users\.noreply\.github\.com|anthropic\.com|example\.(com|org))[A-Za-z0-9.-]+\.[A-Za-z]{2,}'
  machineName      = [Regex]::Escape($env:COMPUTERNAME)
  accountName      = '(?<![A-Za-z])' + [Regex]::Escape($env:USERNAME) + '(?![A-Za-z])'
  usbInstanceId    = 'USB\\VID_[0-9A-F]{4}&PID_[0-9A-F]{4}[^"\s]*\\[0-9a-f]&[0-9a-f]{4,}'
  secretLike       = '(?i)(api[_-]?key|secret|password|passwd|token)\s*[:=]\s*["''][^"'']{8,}'
  privateKey       = '-----BEGIN [A-Z ]*PRIVATE KEY-----'
  githubToken      = 'gh[pousr]_[A-Za-z0-9]{30,}'
  aiAttribution    = '(?i)co-authored-by:\s*claude|generated with \[?claude|claude\.ai/code/session'
}
$R = [ordered]@{ filesScanned = $files.Count; hits = [ordered]@{} }
foreach ($k in $checks.Keys) {
  $h = @(Select-String -Path ($files | ? { $_ -notmatch $bin }) -Pattern $checks[$k] -ErrorAction SilentlyContinue)
  if ($k -eq 'accountName' -and $env:USERNAME.Length -lt 3) { $h = @() }   # too short to scan meaningfully
  $R.hits[$k] = @($h | % { "$($_.Path.Replace($McRoot + '\', '')):$($_.LineNumber)" } | Select -First 15)
}
# Generated/test output that should never be committed
$gen = '(^|/)(data|dist|runs|bin|obj)/|\.(mp4|avi|mov|mkv|trx|log)$|_autotest_analysis\.json$|verification_report\.(json|csv|txt)$|video_audit_report\.txt$|_Setup\.exe$|installer\.zip$|(^|/)_rt_'
$R.generatedFilesInCommitSet = @($files | ? { $_ -match $gen })
$R.largeFiles = @($files | % { $i = Get-Item $_; if ($i.Length -gt 2MB) { "$_ $([math]::Round($i.Length/1MB,1)) MB" } })
$R.gitAuthor = "$(git config user.name) <$(git config user.email)>"
$R.lastCommitTrailers = @(git log -5 --format=%B | Select-String -Pattern '(?i)co-authored|generated with|claude' | % { $_.Line })
$R | ConvertTo-Json -Depth 4
