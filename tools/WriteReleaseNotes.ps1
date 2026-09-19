param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version,
    [string]$Changelog = 'CHANGELOG.md',
    [string]$OutputFile = 'release-notes.md'
)
$ErrorActionPreference = 'Stop'
$text = Get-Content -LiteralPath $Changelog -Raw
$pattern = '(?ms)^## \[' + [regex]::Escape($Version) + '\] - (\d{4}-\d{2}-\d{2})\r?\n(.*?)(?=^## \[|\z)'
$entry = [regex]::Match($text, $pattern)
if (-not $entry.Success -or [string]::IsNullOrWhiteSpace($entry.Groups[2].Value)) {
    throw "A dated, nonempty CHANGELOG.md entry is required for $Version."
}
$notes = $entry.Groups[2].Value.Trim()
# Relative documentation links must resolve from the immutable release tag.
$notes = [regex]::Replace($notes, '\]\((docs/[^)]+)\)', "](https://github.com/astropuzzo/QualitySessionMeter/blob/$Version/`$1)")
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($OutputFile), $notes + "`n", [System.Text.UTF8Encoding]::new($false))
