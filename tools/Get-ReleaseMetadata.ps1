param(
    [Parameter(Mandatory)]
    [string] $Tag,
    [string] $ChangelogPath = (Join-Path $PSScriptRoot '../CHANGELOG.md'),
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/release')
)

$ErrorActionPreference = 'Stop'
if ($Tag -cnotmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw "Neplatný release tag '$Tag'; očakáva sa vX.Y.Z bez úvodných núl."
}
$version = $Tag.Substring(1)
$parts = $version.Split('.')
# Windows Installer uses an 8-bit major/minor and a 16-bit build number.
if ([long]$parts[0] -gt 255 -or [long]$parts[1] -gt 255 -or [long]$parts[2] -gt 65535) {
    throw "Verzia '$version' prekračuje MSI limit 255.255.65535."
}

$changelog = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $ChangelogPath))
$escapedVersion = [regex]::Escape($version)
$headings = [regex]::Matches($changelog, "(?m)^## (?:\[$escapedVersion\]|$escapedVersion)(?: - [0-9]{4}-[0-9]{2}-[0-9]{2})?\s*`r?$")
if ($headings.Count -ne 1) {
    throw "CHANGELOG.md musí obsahovať práve jednu sekciu pre verziu '$version'."
}
$section = $changelog.Substring($headings[0].Index + $headings[0].Length)
$nextHeading = [regex]::Match($section, '(?m)^## ')
if ($nextHeading.Success) {
    $section = $section.Substring(0, $nextHeading.Index)
}
$notes = $section.Trim()
if ($notes -notmatch '(?m)^- \S') {
    throw "Sekcia '$version' v CHANGELOG.md neobsahuje zoznam zmien."
}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$notesPath = Join-Path $OutputDirectory 'release-notes.md'
[IO.File]::WriteAllText($notesPath, $notes + "`r`n", [Text.UTF8Encoding]::new($false))
[pscustomobject]@{ Version = $version; NotesPath = [IO.Path]::GetFullPath($notesPath) }
