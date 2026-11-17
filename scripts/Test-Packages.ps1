param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($id in @('AurorisNet.Umbraco.Drawio', 'AurorisNet.Umbraco.Drawio.Selfhosted')) {
    $file = Join-Path $repo "artifacts/packages/$id.$Version.nupkg"
    $zip = [IO.Compression.ZipFile]::OpenRead($file)
    try {
        $reader = [IO.StreamReader]::new($zip.GetEntry("$id.nuspec").Open())
        try { [xml]$spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $distribution = @($zip.Entries | Where-Object FullName -like 'drawio/*')
        if ($id.EndsWith('.Selfhosted')) {
            if ($distribution.Count -lt 1000 -or -not $zip.GetEntry('drawio/index.html') -or -not $zip.GetEntry('drawio/DRAWIO-LICENSE.txt')) {
                throw 'The Selfhosted distribution or its license is missing.'
            }
            if ($distribution.FullName -match '(^|/)(WEB-INF|META-INF)/|\.map$') { throw 'The distribution includes server-only files or source maps.' }
            $base = $spec.SelectSingleNode("//*[local-name()='dependency'][@id='AurorisNet.Umbraco.Drawio']")
            if ($base.version -ne "[$Version]") { throw "Expected exact base dependency [$Version], got $($base.version)." }
        } else {
            if ($distribution.Count -ne 0) { throw 'The base package contains a draw.io distribution.' }
            $umbraco = $spec.SelectSingleNode("//*[local-name()='dependency'][@id='Umbraco.Cms.Web.Website']")
            if (($umbraco.version -replace '\s', '') -ne '[17.0.0,19.0.0)') {
                throw "Expected Umbraco dependency [17.0.0, 19.0.0), got $($umbraco.version)."
            }
        }
        Write-Host "$id $Version verified ($([Math]::Round((Get-Item -LiteralPath $file).Length / 1MB, 2)) MiB)."
    } finally { $zip.Dispose() }
}
