param(
	[Parameter(Mandatory=$true)] [string] $FilePath,
	[Parameter(Mandatory=$true)] [string] $OutPath
)
try {
	if (-not (Test-Path $FilePath)) { Write-Error "Missing file: $FilePath"; exit 2 }
	$pv = ((Get-Item $FilePath).VersionInfo.ProductVersion).Trim()
	if (-not $pv) { Write-Error "ProductVersion missing for $FilePath"; exit 3 }
	# Write UTF8 without BOM to avoid stray BOM characters when consumed by batch
	$bytes = [System.Text.Encoding]::UTF8.GetBytes($pv)
	[System.IO.File]::WriteAllBytes($OutPath, $bytes)
	exit 0
}
catch {
	Write-Error $_.Exception.Message
	exit 4
}
