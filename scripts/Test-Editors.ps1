param([Parameter(Mandatory)][string]$Version, [switch]$WithApplicationVirtualPathFix)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$project = Join-Path $repo 'tests/Editors/Editors.csproj'
$cache = Join-Path $repo 'artifacts/editor-test-cache'
$packages = @('AurorisNet.Umbraco.Drawio', 'AurorisNet.Umbraco.Drawio.Selfhosted')

# Repacking 0.0.0-dev must exercise the new files. Only evict our packages from this test's private cache.
foreach ($package in $packages) {
    if (-not (Test-Path -LiteralPath (Join-Path $repo "artifacts/packages/$package.$Version.nupkg"))) {
        throw "Pack $package $Version before running the editor tests."
    }
    $cachedPackage = Join-Path $cache $package.ToLowerInvariant()
    if (Test-Path -LiteralPath $cachedPackage) {
        $folder = Get-Item -LiteralPath $cachedPackage
        if ($folder.Parent.FullName -ne $cache -or $folder.LinkType) { throw "Unexpected cache folder: $cachedPackage" }
        Remove-Item -LiteralPath $folder.FullName -Recurse -Force
    }
}

$failed = @()
$testOutput = Join-Path $repo 'artifacts/editor-tests'
if ($WithApplicationVirtualPathFix) {
    $testOutput = Join-Path $testOutput 'with-virtual-path-fix'
    Write-Host 'Testing with AurorisNet.Umbraco.ApplicationVirtualPathFix installed.'
}
foreach ($package in $packages) {
    $output = Join-Path $testOutput $package
    Write-Host "Testing the editor with $package installed."
    dotnet test --project $project -c Release --artifacts-path $output "-p:EditorPackage=$package" `
        "-p:DrawioPackageVersion=$Version" "-p:RestorePackagesPath=$cache" `
        "-p:WithApplicationVirtualPathFix=$($WithApplicationVirtualPathFix.IsPresent)" --no-launch-profile
    if ($LASTEXITCODE -ne 0) { $failed += $package }
}
if ($failed) { throw "Editor tests failed for $($failed -join ', ')." }
