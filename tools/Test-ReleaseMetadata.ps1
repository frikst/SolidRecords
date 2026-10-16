$ErrorActionPreference = 'Stop'
$testDirectory = Join-Path $PSScriptRoot "../artifacts/release-metadata-tests/$([guid]::NewGuid())"
New-Item -ItemType Directory -Force $testDirectory | Out-Null
$changelogPath = Join-Path $testDirectory 'CHANGELOG.md'
$buildPropsPath = Join-Path $testDirectory 'Directory.Build.props'
[IO.File]::WriteAllText($buildPropsPath, '<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>')
$outputDirectory = Join-Path $testDirectory 'output'
$scriptPath = Join-Path $PSScriptRoot 'Get-ReleaseMetadata.ps1'
$fixture = "# Changelog`n`n## Unreleased`n`n- Nevydané.`n`n## [1.2.3] - 2026-10-16`n`n### Added`n`n- Slovenské zmeny.`n`n## [1.2.2] - 2026-10-15`n`n- Staršie zmeny.`n"
[IO.File]::WriteAllText($changelogPath, $fixture)
$result = & $scriptPath -Tag 'v1.2.3' -BuildPropsPath $buildPropsPath -ChangelogPath $changelogPath -OutputDirectory $outputDirectory
$notes = [IO.File]::ReadAllText($result.NotesPath)
if ($result.Version -ne '1.2.3' -or $notes.Trim() -cne "### Added`n`n- Slovenské zmeny.") {
    throw 'Extrahovanie presnej sekcie changelogu zlyhalo.'
}
[IO.File]::WriteAllText($changelogPath, $fixture.Replace("`n", "`r`n"))
& $scriptPath -Tag 'v1.2.3' -BuildPropsPath $buildPropsPath -ChangelogPath $changelogPath -OutputDirectory $outputDirectory | Out-Null

foreach ($tag in @('1.2.3', 'v01.2.3', 'v1.2.3-rc.1', 'v1.2.3+build', 'v1.2.3/extra', 'v256.2.3', 'v1.256.3', 'v1.2.65536', 'v9.9.9')) {
    $rejected = $false
    try { & $scriptPath -Tag $tag -BuildPropsPath $buildPropsPath -ChangelogPath $changelogPath -OutputDirectory $outputDirectory | Out-Null }
    catch { $rejected = $true }
    if (-not $rejected) { throw "Neplatný alebo chýbajúci tag '$tag' nebol odmietnutý." }
}
foreach ($invalid in @("## [1.2.3] - 2026-10-16`n`n### Added`n", "$fixture`n## [1.2.3] - 2026-10-16`n`n- Duplicita.")) {
    [IO.File]::WriteAllText($changelogPath, $invalid)
    $rejected = $false
    try { & $scriptPath -Tag 'v1.2.3' -BuildPropsPath $buildPropsPath -ChangelogPath $changelogPath -OutputDirectory $outputDirectory | Out-Null }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Prázdna alebo duplicitná sekcia nebola odmietnutá.' }
}
# A valid changelog must not mask a missing, ambiguous or mismatched source version.
[IO.File]::WriteAllText($changelogPath, $fixture)
foreach ($props in @(
    '<Project><PropertyGroup><Version>1.2.2</Version></PropertyGroup></Project>',
    '<Project><PropertyGroup><Version>1.2.3-dev</Version></PropertyGroup></Project>',
    '<Project><PropertyGroup><Version>1.2.3+build</Version></PropertyGroup></Project>',
    '<Project><PropertyGroup><Version /></PropertyGroup></Project>',
    '<Project><PropertyGroup /></Project>',
    '<Project><PropertyGroup><Version>1.2.3</Version><Version>1.2.3</Version></PropertyGroup></Project>'
)) {
    [IO.File]::WriteAllText($buildPropsPath, $props)
    $rejected = $false
    try { & $scriptPath -Tag 'v1.2.3' -BuildPropsPath $buildPropsPath -ChangelogPath $changelogPath -OutputDirectory $outputDirectory | Out-Null }
    catch {
        if ($_.Exception.Message -notmatch 'Directory\.Build\.props') { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw "Neplatná centrálna verzia nebola odmietnutá: $props" }
}
Write-Host 'Release metadata: všetky kontroly prešli.'
