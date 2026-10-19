param(
    [string]$LegacyFile = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'Subnautica Launcher\Updates\Updates.cs'),
    [string]$OutputFile = (Join-Path $PSScriptRoot '..\Updates\SNLUpdates.xml')
)

$source = Get-Content -LiteralPath $LegacyFile -Raw
$pattern = 'new\s+UpdateEntry\s*\{\s*Version\s*=\s*"(?<version>[^"]+)"\s*,\s*Title\s*=\s*"(?<title>[^"]*)"\s*,\s*Date\s*=\s*"(?<date>[^"]*)"\s*,\s*Changes\s*=\s*new\[\]\s*\{(?<changes>.*?)\}\s*\}'
$matches = [regex]::Matches($source, $pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
if ($matches.Count -eq 0) { throw 'No legacy update entries were found.' }

$settings = New-Object System.Xml.XmlWriterSettings
$settings.Indent = $true
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)
$writer = [System.Xml.XmlWriter]::Create((Resolve-Path $OutputFile), $settings)
$writer.WriteStartDocument()
$writer.WriteStartElement('updates')

$entryIndex = 0
foreach ($match in $matches) {
    $writer.WriteStartElement('update')
    $version = $match.Groups['version'].Value
    if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') { $version += '.0' }
    $writer.WriteAttributeString('version', $version)
    $writer.WriteAttributeString('title', $match.Groups['title'].Value)
    $writer.WriteAttributeString('date', $match.Groups['date'].Value)
    $writer.WriteStartElement('files')
    if ($entryIndex -eq 0) {
        $releaseFiles = @('Release/SubnauticaLauncher.exe', 'Release/Dependencies/SubnauticaLauncher.ImGuiHost.exe', 'Release/Dependencies/Assets/Fonts/aller.regular.ttf')
        $backgroundDirectory = Join-Path $PSScriptRoot '..\Assets\Backgrounds'
        if (Test-Path -LiteralPath $backgroundDirectory) {
            $releaseFiles += Get-ChildItem -LiteralPath $backgroundDirectory -File | ForEach-Object { "Release/Dependencies/Assets/Backgrounds/$($_.Name)" }
        }
        $iconDirectory = Join-Path $PSScriptRoot '..\Assets\Icons'
        if (Test-Path -LiteralPath $iconDirectory) {
            $releaseFiles += Get-ChildItem -LiteralPath $iconDirectory -File | ForEach-Object { "Release/Dependencies/Assets/Icons/$($_.Name)" }
        }
        foreach ($path in $releaseFiles) {
            $writer.WriteStartElement('file'); $writer.WriteAttributeString('path', $path); $writer.WriteAttributeString('status', 'changed'); $writer.WriteEndElement()
        }
    }
    $writer.WriteEndElement()
    $writer.WriteStartElement('changelog')
    foreach ($change in [regex]::Matches($match.Groups['changes'].Value, '"(?<value>(?:\\.|[^"\\])*)"')) {
        $writer.WriteElementString('change', [regex]::Unescape($change.Groups['value'].Value))
    }
    $writer.WriteEndElement()
    $writer.WriteEndElement()
    $entryIndex++
}

$writer.WriteEndElement()
$writer.WriteEndDocument()
$writer.Dispose()
