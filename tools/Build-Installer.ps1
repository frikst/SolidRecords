param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string] $Version,
    [string] $PublishDirectory = (Join-Path $PSScriptRoot '../artifacts/publish/win-x64'),
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/release'),
    [string] $WixPath = (Join-Path $PSScriptRoot '../artifacts/wix/wix.exe')
)

$ErrorActionPreference = 'Stop'
$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path
foreach ($required in @('SolidRecords.App.exe', 'SolidRecords.App.dll', 'coreclr.dll', 'hostfxr.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishPath $required))) {
        throw "Self-contained publikácia neobsahuje '$required'."
    }
}
$appVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publishPath 'SolidRecords.App.dll')).FileVersion
if ($appVersion -ne "$Version.0") {
    throw "Publikovaná aplikácia má verziu '$appVersion', očakáva sa '$Version.0'."
}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$outputPath = Join-Path (Resolve-Path -LiteralPath $OutputDirectory).Path "SolidRecords-$Version-win-x64.msi"
& $WixPath build (Join-Path $PSScriptRoot '../installer/Package.wxs') -arch x64 -d "ProductVersion=$Version" -d "PublishDirectory=$publishPath" -out $outputPath
if ($LASTEXITCODE -ne 0) {
    throw "WiX build zlyhal s kódom $LASTEXITCODE."
}
