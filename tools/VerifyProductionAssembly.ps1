param(
    [Parameter(Mandatory)][string]$File,
    [Parameter(Mandatory)][version]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$stream = [System.IO.File]::OpenRead((Resolve-Path $File).Path)
$pe = $null
try {
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    $version = $metadata.GetAssemblyDefinition().Version
    if ($version -ne $ExpectedVersion) { throw "Production DLL version $version does not match $ExpectedVersion." }
    foreach ($handle in $metadata.TypeDefinitions) {
        $type = $metadata.GetTypeDefinition($handle)
        $name = $metadata.GetString($type.Namespace) + '.' + $metadata.GetString($type.Name)
        if ($name -match '\.Synthetic\.|\.SyntheticLabDockable$|\.SyntheticResources$|\.QsmControlCenterDockable$|VisualHarness|VisualSlices|DispatcherCheck|SyntheticCheck') {
            throw "Production DLL contains development or removed UI type: $name"
        }
    }
    Write-Output "Production DLL $version verified: no Synthetic Lab, test harness or removed dockable types."
} finally {
    if ($null -ne $pe) { $pe.Dispose() }
    $stream.Dispose()
}
