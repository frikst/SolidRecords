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
foreach ($required in @('SolidRecords.App.exe', 'SolidRecords.App.dll', 'coreclr.dll', 'hostfxr.dll', 'LICENSE')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishPath $required))) {
        throw "Self-contained publikácia neobsahuje '$required'."
    }
}
$licensePath = Join-Path $PSScriptRoot '../LICENSE'
if ((Get-FileHash -LiteralPath $licensePath).Hash -ne (Get-FileHash -LiteralPath (Join-Path $publishPath 'LICENSE')).Hash) {
    throw 'Publikovaná licencia sa nezhoduje s LICENSE v repozitári. Zopakujte dotnet publish.'
}
$appVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publishPath 'SolidRecords.App.dll')).FileVersion
if ($appVersion -ne "$Version.0") {
    throw "Publikovaná aplikácia má verziu '$appVersion', očakáva sa '$Version.0'."
}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$outputPath = Join-Path (Resolve-Path -LiteralPath $OutputDirectory).Path "SolidRecords-$Version-win-x64.msi"
# Licenčný dialóg používa rovnaký text ako publikovaná aplikácia.
$licenseText = [IO.File]::ReadAllText($licensePath)
$rtfText = $licenseText.Replace('\', '\\').Replace('{', '\{').Replace('}', '\}').Replace("`r", '').Replace("`n", "\par`r`n")
$licenseDirectory = Join-Path $PSScriptRoot '../artifacts/installer'
New-Item -ItemType Directory -Force $licenseDirectory | Out-Null
$licenseRtfPath = Join-Path (Resolve-Path -LiteralPath $licenseDirectory).Path 'LICENSE.rtf'
[IO.File]::WriteAllText($licenseRtfPath, ('{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\f0\fs16 ' + $rtfText + '}'), [Text.Encoding]::ASCII)

& $WixPath extension add WixToolset.UI.wixext/5.0.2
if ($LASTEXITCODE -ne 0) {
    throw "Inštalácia rozšírenia WiX UI zlyhala s kódom $LASTEXITCODE."
}
& $WixPath build (Join-Path $PSScriptRoot '../installer/Package.wxs') -arch x64 -culture sk-SK -ext WixToolset.UI.wixext/5.0.2 -d "LicenseRtf=$licenseRtfPath" -d "ProductVersion=$Version" -d "PublishDirectory=$publishPath" -out $outputPath
if ($LASTEXITCODE -ne 0) {
    throw "WiX build zlyhal s kódom $LASTEXITCODE."
}
