# Developing

Requires the .NET 10 SDK (`global.json`), Node.js (built-in test runner, no npm packages), and the `external/drawio` submodule for the Selfhosted project.

```powershell
git submodule update --init --depth 1
dotnet build -c Release
node --test "tests/backoffice/*.test.mjs"
dotnet test --project tests/AurorisNet.Umbraco.Drawio.Tests -c Release
dotnet test --project tests/AurorisNet.Umbraco.Drawio.Tests -c Release -p:UmbracoVersion=17.0.0

# Pack and verify both packages (0.0.0-dev)
dotnet pack src/AurorisNet.Umbraco.Drawio.csproj -c Release -o artifacts/packages
dotnet pack selfhosted/AurorisNet.Umbraco.Drawio.Selfhosted.csproj -c Release -o artifacts/packages
pwsh scripts/Test-Packages.ps1 -Version 0.0.0-dev

# End-to-end Chrome editor tests (add -WithApplicationVirtualPathFix for virtual-path checks)
pwsh scripts/Test-Editors.ps1 -Version 0.0.0-dev
```

## Notes

- Run the full matrix across `UmbracoReleases` in `Directory.Build.props` locally; CI tests one release and treats warnings as errors.
- `Test-Editors.ps1` requires Google Chrome; the hosted test needs internet access and a display (`Xvfb` in CI), while the self-hosted test blocks external traffic.
- To update the bundled draw.io version, update the `external/drawio` submodule and `THIRD-PARTY-NOTICES.md`.

## Releases

Both packages share one `MAJOR.MINOR.PATCH` version. See the shared [release guide](https://github.com/auroris/AurorisNet.Build/blob/main/docs/releasing.md).
