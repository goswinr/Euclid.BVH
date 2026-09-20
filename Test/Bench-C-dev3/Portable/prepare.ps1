param(
    [string] $BaselineRevision = '0.2.0'
)

$ErrorActionPreference = 'Stop'
$benchRoot = Split-Path $PSScriptRoot -Parent
$repoRoot = (Resolve-Path (Join-Path $benchRoot '../..')).Path
$artifactRoot = Join-Path $benchRoot 'dist'
$snapshotRoot = Join-Path $artifactRoot 'baseline-source'

# Keep the user's checkout intact. Never overwrite an existing source snapshot.
if (Test-Path -LiteralPath $snapshotRoot) {
    throw "Baseline snapshot already exists: $snapshotRoot. Use a fresh benchmark checkout to prepare again."
}
New-Item -ItemType Directory -Path $snapshotRoot -Force | Out-Null
$archive = Join-Path $artifactRoot 'baseline-source.zip'
git -C $repoRoot archive --format=zip "--output=$archive" $BaselineRevision
if ($LASTEXITCODE -ne 0) { throw 'git archive failed' }
Expand-Archive -LiteralPath $archive -DestinationPath $snapshotRoot
$snapshotHarness = Join-Path $snapshotRoot 'Test/Bench-C-dev3/Portable'
New-Item -ItemType Directory -Path $snapshotHarness -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Program.fs'), (Join-Path $PSScriptRoot 'Portable.fsproj') -Destination $snapshotHarness

foreach ($variant in @('baseline', 'optimized')) {
    $sourceRoot = if ($variant -eq 'baseline') { $snapshotRoot } else { $repoRoot }
    Push-Location $sourceRoot
    try {
        dotnet tool restore
        if ($LASTEXITCODE -ne 0) { throw 'Tool restore failed' }
        dotnet publish Test/Bench-C-dev3/Portable/Portable.fsproj -c Release -o (Join-Path $artifactRoot "$variant-dotnet")
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
        dotnet fable Test/Bench-C-dev3/Portable/Portable.fsproj --outDir (Join-Path $artifactRoot "$variant-js") --configuration Release --noCache
        if ($LASTEXITCODE -ne 0) { throw 'Fable compilation failed' }
    }
    finally {
        Pop-Location
    }
}
