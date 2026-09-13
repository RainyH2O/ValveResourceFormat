param(
    [Parameter(Mandatory = $true)]
    [string]$CliPath
)

$ErrorActionPreference = 'Stop'
$cli = (Resolve-Path -LiteralPath $CliPath).Path
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$fixture = Join-Path $root 'Tests/Files/point_template_test.vpk'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("vrf-entity-export-" + [Guid]::NewGuid().ToString('N'))

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
    foreach ($entry in @(
        @{ Path = 'maps/point_template_test.vmap_c'; Output = 'map entities.json' },
        @{ Path = 'maps/point_template_test/world.vwrld_c'; Output = 'world entities.json' }
    )) {
        $target = Join-Path $tempRoot $entry.Output
        $result = Invoke-Cli @('-i', $fixture, '--export_entities', '-f', $entry.Path, '-o', $target)
        if ($result.ExitCode -ne 0) {
            throw "CLI entity export failed for $($entry.Path): $($result.Output)"
        }
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
            throw "CLI did not write $target"
        }
        $json = Get-Content -Raw -Encoding utf8 -LiteralPath $target | ConvertFrom-Json
        if ($json -isnot [System.Array] -or $json.Count -eq 0) {
            throw "CLI did not write a non-empty entity array for $($entry.Path)"
        }
    }

    $map = Get-Content -Raw -Encoding utf8 -LiteralPath (Join-Path $tempRoot 'map entities.json') | ConvertFrom-Json
    $world = Get-Content -Raw -Encoding utf8 -LiteralPath (Join-Path $tempRoot 'world entities.json') | ConvertFrom-Json
    if ((@($map | ConvertTo-Json -Depth 32 -Compress) -join "`n") -ne (@($world | ConvertTo-Json -Depth 32 -Compress) -join "`n")) {
        throw 'Map and World entity exports differ for the fixture'
    }
    if (-not ($map | Where-Object { $_.classname -eq 'point_template' })) {
        throw 'Entity export omitted point_template'
    }
    if (-not ($map | Where-Object { $_.classname -eq 'prop_dynamic' })) {
        throw 'Entity export omitted template members'
    }

    $preservedOutput = Join-Path $tempRoot 'preserved.json'
    [System.IO.File]::WriteAllText($preservedOutput, 'preserve this output')
    foreach ($arguments in @(
        @('-i', $fixture, '--export_entities', '-o', $preservedOutput),
        @('-i', $fixture, '--export_entities', '-f', 'maps/*.vmap_c', '-o', $preservedOutput),
        @('-i', $fixture, '--export_entities', '-f', 'maps/missing.vmap_c', '-o', $preservedOutput),
        @('--export_entities_map', '../invalid', '-o', $preservedOutput)
    )) {
        $result = Invoke-Cli $arguments
        if ($result.ExitCode -eq 0) {
            throw "CLI unexpectedly accepted invalid entity export arguments: $($arguments -join ' ')"
        }
        if ([System.IO.File]::ReadAllText($preservedOutput) -ne 'preserve this output') {
            throw 'Failed entity export replaced an existing output file'
        }
    }

    $result = Invoke-Cli @('-i', $fixture, '--vpk_list')
    if ($result.ExitCode -ne 0) {
        throw "Existing VPK list command failed: $($result.Output)"
    }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
