[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet build XivType/XivType.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    dotnet run --project tests/XivType.Replay.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed.' }

    $manifest = Get-Content XivType/bin/Release/XivType.json -Raw | ConvertFrom-Json
    $version = $manifest.AssemblyVersion
    if ($manifest.InternalName -ne 'XivType' -or $manifest.DalamudApiLevel -ne 15) {
        throw 'Unexpected plugin identity or API level.'
    }
    if ($manifest.RepoUrl -ne 'https://github.com/kuchris/xivtype' -or
        $manifest.IconUrl -ne "https://raw.githubusercontent.com/kuchris/xivtype/$version/images/icon.png") {
        throw 'Manifest repository/icon URLs do not match this release.'
    }

    Add-Type -AssemblyName System.Drawing
    $image = [Drawing.Image]::FromFile((Join-Path $root 'images/icon.png'))
    try {
        if ($image.Width -ne 512 -or $image.Height -ne 512) { throw 'Icon must be 512 x 512.' }
    } finally { $image.Dispose() }

    New-Item -ItemType Directory -Path artifacts -Force | Out-Null
    $archive = Join-Path $root "artifacts/XivType-$version.zip"
    Copy-Item XivType/bin/Release/XivType/latest.zip $archive
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($file in @('XivType.dll', 'XivType.json', 'icon.png')) {
            $entry = $zip.GetEntry($file)
            if ($null -eq $entry) { throw "Missing ZIP entry: $file" }
            $stream = $entry.Open()
            try { $packedHash = ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($stream))).Replace('-', '') }
            finally { $stream.Dispose() }
            if ($packedHash -ne (Get-FileHash "XivType/bin/Release/$file").Hash) {
                throw "ZIP does not contain the current build: $file"
            }
        }
    } finally { $zip.Dispose() }

    $hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $root 'artifacts/SHA256SUMS.txt'), "$hash  $(Split-Path $archive -Leaf)`n")
    Write-Output "Verified $archive"
} finally { Pop-Location }
