$ErrorActionPreference = 'Stop'

function Read-Text([string]$p) { return [IO.File]::ReadAllText((Join-Path $PWD $p)) }
function Write-Text([string]$p, [string]$s) { [IO.File]::WriteAllText((Join-Path $PWD $p), $s, [Text.UTF8Encoding]::new($false)) }
function Replace-Checked([string]$s, [string]$old, [string]$new, [string]$label) {
    if (-not $s.Contains($old)) { throw "Missing expected text for $label" }
    return $s.Replace($old, $new)
}
function Remove-Style([string]$s, [string]$key) {
    $pattern = '(?s)\s*<Style x:Key="' + [regex]::Escape($key) + '".*?</Style>\s*'
    $matches = [regex]::Matches($s, $pattern)
    if ($matches.Count -ne 1) { throw "Expected exactly one shared style $key, found $($matches.Count)" }
    return [regex]::Replace($s, $pattern, "`r`n", 1)
}

# Main QSM dockable. Keep only immutable/freezable resources in the dictionary; former Style
# properties become local template values and host theme references remain DynamicResource.
$p = 'UI/Resources.xaml'
$s = Read-Text $p
$s = Remove-Style $s 'QsmCardStyle'
$s = Remove-Style $s 'QsmSectionTitle'
$s = $s.Replace('Style="{StaticResource QsmCardStyle}"', 'Background="{DynamicResource SecondaryBackgroundBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="8" Padding="10" Margin="0,0,0,8"')
$s = $s.Replace('Style="{StaticResource QsmSectionTitle}"', 'Foreground="{DynamicResource ButtonForegroundBrush}" Opacity="0.78" FontSize="11" FontWeight="SemiBold" TextAlignment="Left"')
$s = Replace-Checked $s '<ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">' '<ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" Loaded="DockableLoaded">' 'main dockable root Loaded'
if ($s -match 'QsmCardStyle|QsmSectionTitle') { throw 'Shared main dockable style reference remains.' }
Write-Text $p $s

# Native N.I.N.A.-style options page. All former helper styles are expanded locally.
$p = 'UI/OptionsHelpResources.xaml'
$s = Read-Text $p
foreach ($key in @('QsmSectionHeader','QsmSubHeader','QsmHint','QsmOptionRow')) { $s = Remove-Style $s $key }
$s = $s.Replace('Style="{StaticResource QsmSectionHeader}"', 'FontSize="18" FontWeight="Normal" TextAlignment="Left"')
$s = $s.Replace('Style="{StaticResource QsmSubHeader}"', 'FontWeight="SemiBold" TextAlignment="Left"')
$s = $s.Replace('Style="{StaticResource QsmHint}"', 'Opacity="0.72" FontSize="11" TextWrapping="Wrap" TextAlignment="Left"')
$s = $s.Replace('Style="{StaticResource QsmOptionRow}"', 'Margin="14,2,0,2"')
$s = Replace-Checked $s '<ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" Background="{DynamicResource BackgroundBrush}">' '<ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" Background="{DynamicResource BackgroundBrush}" Loaded="OptionsRootLoaded">' 'options root Loaded'
if ($s -match 'QsmSectionHeader|QsmSubHeader|QsmHint|QsmOptionRow') { throw 'Shared options style reference remains.' }
Write-Text $p $s

# Control Center.
$p = 'UI/ControlCenterResources.xaml'
$s = Read-Text $p
foreach ($key in @('QsmCcSectionTitle','QsmCcHelp')) { $s = Remove-Style $s $key }
$s = $s.Replace('Style="{StaticResource QsmCcSectionTitle}"', 'FontWeight="SemiBold" FontSize="13" Margin="0,0,0,8" TextAlignment="Left"')
$s = $s.Replace('Style="{StaticResource QsmCcHelp}"', 'Opacity="0.68" TextWrapping="Wrap" FontSize="11" TextAlignment="Left"')
$s = Replace-Checked $s '<ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">' '<ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" Loaded="ControlCenterLoaded">' 'control center root Loaded'
if ($s -match 'QsmCcSectionTitle|QsmCcHelp') { throw 'Shared Control Center style reference remains.' }
Write-Text $p $s

# Synthetic Lab field-test dockable.
$p = 'UI/SyntheticResources.xaml'
$s = Read-Text $p
$s = Remove-Style $s 'LabCardStyle'
$s = $s.Replace('Style="{StaticResource LabCardStyle}"', 'Background="{DynamicResource SecondaryBackgroundBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="8" Padding="12" Margin="0,0,0,8"')
$s = Replace-Checked $s '<ScrollViewer VerticalScrollBarVisibility="Auto">' '<ScrollViewer VerticalScrollBarVisibility="Auto" Loaded="SyntheticRootLoaded">' 'synthetic root Loaded'
if ($s -match 'LabCardStyle') { throw 'Shared Synthetic Lab style reference remains.' }
Write-Text $p $s

# Version bump.
$p = 'QualitySessionMeter.csproj'
$s = Read-Text $p
if (-not $s.Contains('1.3.0.5')) { throw 'Expected 1.3.0.5 in csproj.' }
$s = $s.Replace('1.3.0.5', '1.3.0.6')
Write-Text $p $s

# Changelog.
$p = 'CHANGELOG.md'
$s = Read-Text $p
$marker = '## [1.3.0.5]'
if (-not $s.Contains($marker)) { throw '1.3.0.5 changelog marker missing.' }
$entry = @'
## [1.3.0.6] - 2026-09-09

### AvalonDock dispatcher-style crash hotfix

- Fixed a real-host N.I.N.A. 3.3.0.1057 `XamlParseException` where AvalonDock materialized QSM templates on a different dispatcher and WPF rejected a shared plugin `Style` (`Style.CheckTargetType`: "The calling thread cannot access this object because a different thread owns it").
- Removed all shared keyed `Style` objects from QSM dockable/options/synthetic ResourceDictionaries. Former style properties are now local template properties; theme normalization starts from each visual root's `Loaded` event on the owning UI dispatcher.
- Removed QSM `EventSetter`/`AddLoadedHandler` mutation of shared styles.
- Preserved N.I.N.A. theme following through `DynamicResource` and element-local `SetResourceReference`, and preserved the adaptive Frame # adorner attachment.
- Added release gates that reject reintroduction of shared keyed styles or style-mutation helpers in dispatcher-sensitive QSM UI resources.

---

'@
$s = $s.Replace($marker, $entry + $marker)
Write-Text $p $s

# Permanent CI/store-release gates. This turns the real-host failure into a forbidden source pattern.
foreach ($workflowPath in @('.github/workflows/build.yml','.github/workflows/nina-release.yml')) {
    $s = Read-Text $workflowPath
    $anchor = '          $resourcesCode = Get-Content UI/Resources.xaml.cs -Raw'
    if (-not $s.Contains($anchor)) { throw "Gate anchor missing in $workflowPath" }
    $gate = @'
          $dispatcherSensitiveXaml = @(
            'UI/Resources.xaml',
            'UI/OptionsHelpResources.xaml',
            'UI/ControlCenterResources.xaml',
            'UI/SyntheticResources.xaml'
          )
          foreach ($xamlPath in $dispatcherSensitiveXaml) {
            if (Test-Path $xamlPath) {
              $xamlText = Get-Content $xamlPath -Raw
              if ($xamlText -match '<Style\s+x:Key=') { throw "Dispatcher-sensitive shared keyed Style found in $xamlPath" }
            }
          }
          foreach ($codePath in @('UI/Resources.xaml.cs','UI/OptionsHelpResources.xaml.cs','UI/ControlCenterResources.xaml.cs','UI/SyntheticResources.xaml.cs')) {
            if (Test-Path $codePath) {
              $codeText = Get-Content $codePath -Raw
              if ($codeText -match 'AddLoadedHandler\(' -or $codeText -match 'EventSetter\(') { throw "Cross-dispatcher Style mutation helper found in $codePath" }
            }
          }
'@
    $s = $s.Replace($anchor, $anchor + "`r`n" + $gate.TrimEnd())
    Write-Text $workflowPath $s
}

# Assertions against both XAML and the manually rewritten code-behind.
foreach ($xamlPath in @('UI/Resources.xaml','UI/OptionsHelpResources.xaml','UI/ControlCenterResources.xaml','UI/SyntheticResources.xaml')) {
    if ((Read-Text $xamlPath) -match '<Style\s+x:Key=') { throw "Shared keyed Style still present in $xamlPath" }
}
foreach ($codePath in @('UI/Resources.xaml.cs','UI/OptionsHelpResources.xaml.cs','UI/ControlCenterResources.xaml.cs','UI/SyntheticResources.xaml.cs')) {
    $code = Read-Text $codePath
    if ($code -match 'AddLoadedHandler\(' -or $code -match 'EventSetter\(') { throw "Shared Style mutation still present in $codePath" }
}

Write-Host 'Dispatcher-style XAML/version/gate transform completed.'
