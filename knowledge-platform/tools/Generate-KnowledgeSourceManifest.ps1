[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$SourceRoot,

    [Parameter(Mandatory)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path -LiteralPath $SourceRoot).Path.TrimEnd([char[]]@('\', '/'))

function Get-RelativeSourcePath([string]$fullName) {
    $relative = $fullName.Substring($root.Length).TrimStart([char[]]@('\', '/'))
    return $relative.Replace('\', '/')
}

$files = Get-ChildItem -LiteralPath $root -File -Recurse |
    Sort-Object { Get-RelativeSourcePath $_.FullName }

$entries = @(
    foreach ($file in $files) {
        [ordered]@{
            relativePath = Get-RelativeSourcePath $file.FullName
            sizeBytes = [int64]$file.Length
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
)

$canonical = ($entries | ForEach-Object {
    $_.relativePath + "|" + $_.sizeBytes + "|" + $_.sha256
}) -join [char]10
$manifestHasher = [System.Security.Cryptography.SHA256]::Create()
try {
    $manifestHash = [BitConverter]::ToString(
        $manifestHasher.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($canonical))
    ).Replace("-", "").ToLowerInvariant()
}
finally {
    $manifestHasher.Dispose()
}
$totalBytes = [int64]0
foreach ($entry in $entries) {
    $totalBytes += [int64]$entry["sizeBytes"]
}
$manifest = [ordered]@{
    schemaVersion = 1
    sourceRootName = (Split-Path -Leaf $root)
    fileCount = $entries.Count
    totalBytes = $totalBytes
    manifestSha256 = $manifestHash
    files = $entries
}

$output = [System.IO.Path]::GetFullPath($OutputPath)
$directory = Split-Path -Parent $output
if ($directory) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$json = $manifest | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($output, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
Write-Output ("Wrote {0} files, {1} bytes, manifest SHA-256 {2}" -f $manifest.fileCount, $manifest.totalBytes, $manifest.manifestSha256)