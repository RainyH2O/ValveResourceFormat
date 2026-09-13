param(
    [Parameter(Mandatory = $true)]
    [string]$CliPath
)

$ErrorActionPreference = 'Stop'
$cli = (Resolve-Path -LiteralPath $CliPath).Path
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("vrf-entity-import-" + [Guid]::NewGuid().ToString('N'))

function Invoke-Cli {
    param([string[]]$Arguments)

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = & $cli @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference
    return @{ ExitCode = $exitCode; Output = [string]::Join("`n", $output) }
}

try {
    New-Item -ItemType Directory -Path $tempRoot | Out-Null
    $source = Join-Path $tempRoot 'source entities.json'
    [System.IO.File]::WriteAllText($source, @'
{
  "formatVersion": 1,
  "entities": [
    {
      "classname": "trigger_once",
      "targetname": "start",
      "origin": "0 0 32",
      "box": { "size": [128, 128, 64] },
      "connections": [ { "output": "OnStartTouch", "target": "relay", "input": "Trigger" } ]
    },
    {
      "classname": "logic_relay",
      "targetname": "relay",
      "origin": [128, 0, 32],
      "connections": [ { "output": "OnTrigger", "target": "external_door", "input": "Open" } ]
    }
  ]
}
'@)
    $target = Join-Path $tempRoot 'output map.vmap'
    $result = Invoke-Cli @('--import_entities', '-i', $source, '-o', $target)
    if ($result.ExitCode -ne 0) {
        throw "CLI entity import failed: $($result.Output)"
    }
    $bytes = [System.IO.File]::ReadAllBytes($target)
    $header = [System.Text.Encoding]::ASCII.GetString($bytes, 0, [Math]::Min(128, $bytes.Length))
    if ($bytes.Length -lt 128 -or $header -notmatch '^<!-- dmx encoding binary 9 format vmap ') {
        throw 'CLI did not write a binary VMAP document'
    }
    if ($result.Output -notmatch 'external_door') {
        throw 'CLI did not report the unresolved external target'
    }

    $result = Invoke-Cli @('--import_entities', '-i', $source, '-o', $target, '--quiet')
    if ($result.ExitCode -ne 0 -or $result.Output -match 'external_door' -or $result.Output -notmatch '2') {
        throw "Quiet entity import did not report only its summary: $($result.Output)"
    }

    $invalidSource = Join-Path $tempRoot 'invalid.json'
    [System.IO.File]::WriteAllText($invalidSource, '{ "formatVersion": 1, "entities": [ { "classname": "logic_relay", "origin": [1, 2] } ] }')
    $samePath = Join-Path $tempRoot 'same.vmap'
    [System.IO.File]::WriteAllText($samePath, [System.IO.File]::ReadAllText($source))
    $preservedOutput = Join-Path $tempRoot 'preserved.vmap'
    [System.IO.File]::WriteAllText($preservedOutput, 'preserve this output')
    foreach ($arguments in @(
        @('--import_entities', '-i', $invalidSource, '-o', $preservedOutput),
        @('--import_entities', '-i', (Join-Path $tempRoot 'missing.json'), '-o', $preservedOutput),
        @('--import_entities', '-i', $source, '-o', $preservedOutput, '--export_entities'),
        @('--import_entities', '-i', $source, '-o', $preservedOutput, '--export_entities_map', 'de_dust2'),
        @('--import_entities', '-i', $source, '-o', $preservedOutput, '-f', '*'),
        @('--import_entities', '-i', $source, '-o', $preservedOutput, '-e', 'json'),
        @('--import_entities', '-i', $source, '-o', $preservedOutput, '--kv_flatten'),
        @('--import_entities', '-i', $source, '-o', $preservedOutput, '--shader_list_combos'),
        @('--import_entities', '-i', $source, '-o', $preservedOutput, '--vpk_create', (Join-Path $tempRoot 'invalid.vpk')),
        @('--import_entities', '-i', $source, '-o', $preservedOutput, '--decompile'),
        @('--import_entities', '-i', $source, '-o', $preservedOutput, '--recursive'),
        @('--import_entities', '-i', $source, '-o', $preservedOutput, '--threads', '2')
    )) {
        $result = Invoke-Cli $arguments
        if ($result.ExitCode -eq 0) {
            throw "CLI unexpectedly accepted invalid entity import arguments: $($arguments -join ' ')"
        }
        if ([System.IO.File]::ReadAllText($preservedOutput) -ne 'preserve this output') {
            throw 'Failed entity import replaced an existing output file'
        }
    }

    foreach ($arguments in @(
        @('--import_entities', '-i', $source),
        @('--import_entities', '-i', $source, '-o', '-'),
        @('--import_entities', '-i', $source, '-o', $source),
        @('--import_entities', '-i', $samePath, '-o', $samePath),
        @('--import_entities', '-i', $source, '-o', (Join-Path $tempRoot 'wrong.json')),
        @('--import_entities', '-i', $tempRoot, '-o', (Join-Path $tempRoot 'new.vmap')),
        @('--import_entities', '-i', $invalidSource, '-o', (Join-Path $tempRoot 'uncreated/map.vmap'))
    )) {
        $result = Invoke-Cli $arguments
        if ($result.ExitCode -eq 0) {
            throw "CLI unexpectedly accepted invalid input or output: $($arguments -join ' ')"
        }
    }
    if ([System.IO.File]::ReadAllText($samePath) -ne [System.IO.File]::ReadAllText($source)) {
        throw 'Entity import replaced its input file'
    }
    foreach ($unexpected in @('uncreated', 'wrong.json', 'new.vmap', 'invalid.vpk')) {
        if (Test-Path -LiteralPath (Join-Path $tempRoot $unexpected)) {
            throw "Invalid entity import created $unexpected"
        }
    }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        $expectedPrefix = Join-Path ([System.IO.Path]::GetTempPath()) 'vrf-entity-import-'
        if (-not [System.IO.Path]::GetFullPath($tempRoot).StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'Unexpected entity import test directory.'
        }
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
