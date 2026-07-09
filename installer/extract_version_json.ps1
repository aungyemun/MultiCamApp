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
	# Inno Setup VersionInfoVersion/VersionInfoProductVersion requires purely numeric
	# x.y.z.w, and installer\MultiCamApp.iss always builds AppVersionNumeric as
	# "<version>.<build>" regardless of whether the human-readable version string
	# carries a pre-release suffix (e.g. "1.2.1-alpha") or not (e.g. "1.2.33"). Strip
	# any suffix and always append build so this always agrees with the compiled
	# installer's file ProductVersion for comparison.
	$numericBase = ($ver -split '-')[0]   # e.g. "1.2.1" or "1.2.33"
	$verOut = "$numericBase.$build"        # e.g. "1.2.1.216" or "1.2.33.253"
	$out = "$verOut|$build|$stage"
	# Write UTF8 without BOM to avoid introducing stray characters when read by batch scripts
	$bytes = [System.Text.Encoding]::UTF8.GetBytes($out)
	[System.IO.File]::WriteAllBytes($OutPath, $bytes)
	exit 0
}
catch {
	Write-Error $_.Exception.Message
	exit 4
}
