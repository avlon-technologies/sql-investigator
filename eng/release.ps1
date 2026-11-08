<#
.SYNOPSIS
  Builds the SQL Investigator Windows x64 release zip from a clean checkout, reproducibly.

.DESCRIPTION
  Publishes src/SqlInvestigator.McpServer self-contained for win-x64 (net10.0, as bound by BindLocalMCPServer),
  then writes artifacts/release/SqlInvestigator-<version>-win-x64.zip, its .sha256, and RELEASE.json. The zip
  holds the program files, README.md (docs/USER-GUIDE.md), LICENSE.txt, THIRD-PARTY-NOTICES.md, licenses/ and VERSION.txt.

  Reproducible: deterministic compilation with ContinuousIntegrationBuild path mapping, the commit SHA as the
  source revision, zip entries in ordinal order, and every entry timestamped with the commit time. Two runs at
  the same commit with the same SDK produce the same zip hash. -Verify builds twice and checks that.

.PARAMETER AllowDirty
  Build although the working tree has uncommitted changes. The build always uses a clean checkout of HEAD, never the working copy.
.PARAMETER Verify
  Build twice into separate folders and fail unless the two zip hashes are identical.
#>
[CmdletBinding()]
param([switch]$AllowDirty, [switch]$Verify)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    $dirty = [bool](git status --porcelain)
    if ($dirty -and -not $AllowDirty) { throw 'The working tree has uncommitted changes. Commit them, or pass -AllowDirty for a labelled non-release build.' }
    $sha = (git rev-parse HEAD).Trim()
    $commitTime = [DateTimeOffset]::FromUnixTimeSeconds([long](git log -1 --format=%ct)).UtcDateTime
    $version = ([xml](Get-Content 'src/SqlInvestigator.McpServer/SqlInvestigator.McpServer.csproj')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $sdk = (dotnet --version).Trim()
    $name = "SqlInvestigator-$version-win-x64"
    $out = Join-Path $root 'artifacts/release'

    # Build from a clean checkout of the commit, never the working copy: line endings and untracked files in a
    # working copy differ from a fresh checkout and would change the hash.
    function Build-Once([string]$work) {
        if (Test-Path $work) { Remove-Item $work -Recurse -Force }
        $publish = Join-Path $work 'publish'
        $src = Join-Path $work 'src'
        git worktree add --detach --quiet $src $sha
        if ($LASTEXITCODE -ne 0) { throw "git worktree add failed ($LASTEXITCODE)" }
        try {
        dotnet publish (Join-Path $src 'src/SqlInvestigator.McpServer') -c Release -r win-x64 --self-contained true `
            -p:ContinuousIntegrationBuild=true -p:Deterministic=true -p:SourceRevisionId=$sha `
            -p:DebugType=none -p:GenerateDocumentationFile=false -o $publish --nologo -v quiet | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

        Copy-Item (Join-Path $src 'docs/USER-GUIDE.md') (Join-Path $publish 'README.md')
        Copy-Item (Join-Path $src 'LICENSE') (Join-Path $publish 'LICENSE.txt')
        Copy-Item (Join-Path $src 'THIRD-PARTY-NOTICES.md') (Join-Path $publish 'THIRD-PARTY-NOTICES.md')
        Copy-Item (Join-Path $src 'eng/licenses') (Join-Path $publish 'licenses') -Recurse
        } finally { git worktree remove --force $src }
        $label = if ($dirty) { "$sha (built from HEAD; the working tree had uncommitted changes)" } else { $sha }
        @(
            "SQL Investigator $version"
            "commit   $label"
            "built    $($commitTime.ToString('yyyy-MM-ddTHH:mm:ssZ')) (commit time)"
            "sdk      $sdk"
            'platform Windows x64, self-contained .NET 10 (no .NET install needed)'
        ) -join "`n" | Set-Content -NoNewline -Encoding ascii (Join-Path $publish 'VERSION.txt')

        $zip = Join-Path $work "$name.zip"
        $stream = [IO.File]::Open($zip, 'CreateNew')
        try {
            $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
            $files = Get-ChildItem $publish -Recurse -File | ForEach-Object { $_.FullName.Substring($publish.Length + 1).Replace('\', '/') } | Sort-Object { $_ } -Culture ([Globalization.CultureInfo]::InvariantCulture) -CaseSensitive
            foreach ($rel in $files) {
                $entry = $archive.CreateEntry($rel, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new($commitTime)
                $target = $entry.Open()
                try { $source = [IO.File]::OpenRead((Join-Path $publish $rel)); try { $source.CopyTo($target) } finally { $source.Dispose() } } finally { $target.Dispose() }
            }
            $archive.Dispose()
        } finally { $stream.Dispose() }
        return @{ Zip = $zip; Hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant(); Files = $files.Count }
    }

    $first = Build-Once (Join-Path $root 'artifacts/release-work/a')
    if ($Verify) {
        $second = Build-Once (Join-Path $root 'artifacts/release-work/b')
        if ($first.Hash -ne $second.Hash) { throw "Not reproducible: $($first.Hash) vs $($second.Hash)" }
        Write-Host "Reproducible: two builds both hash to $($first.Hash)"
    }

    New-Item -ItemType Directory -Force $out | Out-Null
    Copy-Item $first.Zip (Join-Path $out "$name.zip") -Force
    "$($first.Hash)  $name.zip" | Set-Content -NoNewline -Encoding ascii (Join-Path $out "$name.zip.sha256")
    [ordered]@{
        name = $name; version = $version; commit = $sha; dirty = $dirty; commitTime = $commitTime.ToString('yyyy-MM-ddTHH:mm:ssZ')
        sdk = $sdk; runtimeIdentifier = 'win-x64'; selfContained = $true; files = $first.Files
        sha256 = $first.Hash; reproducibilityVerified = [bool]$Verify
    } | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $out 'RELEASE.json')
    Write-Host "$name.zip  sha256 $($first.Hash)  ($($first.Files) files)  -> $out"
} finally { Pop-Location }
