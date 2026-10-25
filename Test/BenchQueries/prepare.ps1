param(
    # The committed revision to compare the working tree against.
    [string] $BaselineRevision = 'HEAD',
    # The variants to build: 'baseline' from the revision above, any other name from the working tree.
    [string[]] $Variants = @('baseline', 'optimized')
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$artifactRoot = Join-Path $PSScriptRoot 'dist'
$snapshotRoot = Join-Path $artifactRoot 'baseline-source'
$revisionFile = Join-Path $artifactRoot 'baseline-revision.txt'
$revision = (git -C $repoRoot rev-parse $BaselineRevision).Trim()
if ($LASTEXITCODE -ne 0) { throw "Unknown revision $BaselineRevision" }

if ($Variants -contains 'baseline') {
    if (Test-Path -LiteralPath $snapshotRoot) {
        # Reuse the snapshot only if it is of the same revision. It is never overwritten.
        $existing = if (Test-Path -LiteralPath $revisionFile) { (Get-Content -LiteralPath $revisionFile).Trim() } else { '' }
        if ($existing -ne $revision) {
            throw "The baseline snapshot in $snapshotRoot is of revision '$existing', not $revision. Delete Test/BenchQueries/dist to prepare again."
        }
    }
    else {
        # Keep the user's checkout intact: the baseline is an archive of the revision, not a checkout.
        New-Item -ItemType Directory -Path $snapshotRoot -Force | Out-Null
        $archive = Join-Path $artifactRoot 'baseline-source.zip'
        git -C $repoRoot archive --format=zip "--output=$archive" $revision
        if ($LASTEXITCODE -ne 0) { throw 'git archive failed' }
        Expand-Archive -LiteralPath $archive -DestinationPath $snapshotRoot
        Set-Content -LiteralPath $revisionFile -Value $revision
    }
    # The harness is the same for all variants, so the current one is copied into the snapshot.
    $snapshotHarness = Join-Path $snapshotRoot 'Test/BenchQueries'
    New-Item -ItemType Directory -Path $snapshotHarness -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Program.fs'), (Join-Path $PSScriptRoot 'BenchQueries.fsproj') -Destination $snapshotHarness
}

foreach ($variant in $Variants) {
    $sourceRoot = if ($variant -eq 'baseline') { $snapshotRoot } else { $repoRoot }
    Push-Location $sourceRoot
    try {
        dotnet tool restore
        if ($LASTEXITCODE -ne 0) { throw 'Tool restore failed' }
        dotnet publish Test/BenchQueries/BenchQueries.fsproj -c Release -o (Join-Path $artifactRoot "$variant-dotnet")
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
        dotnet fable Test/BenchQueries/BenchQueries.fsproj --outDir (Join-Path $artifactRoot "$variant-js") --configuration Release --noCache
        if ($LASTEXITCODE -ne 0) { throw 'Fable compilation failed' }
    }
    finally {
        Pop-Location
    }
}
