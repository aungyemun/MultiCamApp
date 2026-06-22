param(
	[Parameter(Mandatory=$true)] [string] $JsonPath,
	[Parameter(Mandatory=$true)] [string] $OutPath
)
try {
	if (-not (Test-Path $JsonPath)) { Write-Error "Missing json: $JsonPath"; exit 2 }
	$j = Get-Content $JsonPath -Raw | ConvertFrom-Json
	$ver = $j.version -as [string]
	$build = $j.build -as [string]
	$stage = $j.stage -as [string]
	if (-not $ver) { Write-Error "version missing in $JsonPath"; exit 3 }
	$out = "$ver|$build|$stage"
	# Write UTF8 without BOM to avoid introducing stray characters when read by batch scripts
	$bytes = [System.Text.Encoding]::UTF8.GetBytes($out)
	[System.IO.File]::WriteAllBytes($OutPath, $bytes)
	exit 0
}
catch {
	Write-Error $_.Exception.Message
	exit 4
}
