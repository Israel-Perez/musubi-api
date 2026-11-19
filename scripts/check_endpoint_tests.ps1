#Requires -Version 5.1
<#
.SYNOPSIS
  Guard de pruebas por endpoint (SCRUM-120): cada fila de docs/endpoints.md tiene su prueba.
.DESCRIPTION
  -Suite IntegrationTest | SecurityTest
    Lee TestResults/endpoint-coverage/<Suite>.txt, que escribe esa suite al terminar (EndpointCoverage: los
    endpoints que alguna prueba toco de verdad, "VERBO plantilla"). FAIL si una fila no aparece.
  -Suite UnitTest
    Las pruebas unitarias son de servicios, no de rutas: cada fila cuya columna Spec nombra una HU debe tener, en
    al menos una de sus HU, una ruta UnitTest/ en la columna Tests de docs/trazabilidad.md.
  Las excepciones van en $Waivers con su motivo (una fila que ninguna prueba de esa suite puede tocar).
  Compatible con PowerShell 5.1 y pwsh. ASCII puro.
.EXAMPLE
  dotnet test --project IntegrationTest/IntegrationTest.csproj -c Release --no-build
  pwsh scripts/check_endpoint_tests.ps1 -Suite IntegrationTest
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('IntegrationTest', 'SecurityTest', 'UnitTest')]
    [string]$Suite
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$docPath = Join-Path $repoRoot 'docs/endpoints.md'
$trazPath = Join-Path $repoRoot 'docs/trazabilidad.md'

# Filas que una suite no puede tocar, con su motivo. Agregar una aqui es una decision de revision, no un atajo.
$Waivers = @{
    'IntegrationTest' = @{
        'GET /media/{**path}' = 'Solo Development (Media:Provider=InMemory); en la nube CloudFront sirve /media desde S3.'
    }
    'SecurityTest'    = @{
        'GET /media/{**path}' = 'Solo Development (Media:Provider=InMemory); en la nube CloudFront sirve /media desde S3.'
    }
    'UnitTest'        = @{}
}

function Format-Route([string]$route) {
    $r = $route.Replace('v{version:apiVersion}', 'v1')
    if (-not $r.StartsWith('/')) { $r = '/' + $r }
    if ($r.Length -gt 1) { $r = $r.TrimEnd('/') }
    $parts = [regex]::Split($r, '(\{[^}]*\})')
    for ($i = 0; $i -lt $parts.Count; $i++) {
        if (-not $parts[$i].StartsWith('{')) { $parts[$i] = $parts[$i].ToLowerInvariant() }
    }
    return (-join $parts)
}

if (-not (Test-Path -LiteralPath $docPath)) {
    Write-Output 'endpoint-tests: FAIL (falta docs/endpoints.md)'
    exit 1
}

# Filas: verbo, ruta y columna Spec (la cuarta).
$rows = @()
foreach ($line in (Get-Content -LiteralPath $docPath)) {
    $m = [regex]::Match($line, '^\|\s*(GET|POST|PUT|PATCH|DELETE|WS)\s*\|\s*(/[^\s|]*)\s*\|[^|]*\|\s*([^|]*)\|')
    if ($m.Success) {
        $rows += [pscustomobject]@{
            Key  = ('{0} {1}' -f $m.Groups[1].Value, (Format-Route $m.Groups[2].Value))
            Spec = $m.Groups[3].Value.Trim()
        }
    }
}

$waived = $Waivers[$Suite]
$missing = New-Object System.Collections.Generic.List[string]

if ($Suite -eq 'UnitTest') {
    $traz = Get-Content -LiteralPath $trazPath -Raw
    foreach ($row in $rows) {
        if ($waived.ContainsKey($row.Key)) { continue }
        $hus = @([regex]::Matches($row.Spec, 'HU-\d+') | ForEach-Object { $_.Value } | Select-Object -Unique)
        # Filas de plataforma (spec 01-xx / 02-xx): sin HU, las cubren las pruebas de su spec.
        if ($hus.Count -eq 0) { continue }
        $covered = $false
        foreach ($hu in $hus) {
            $tm = [regex]::Match($traz, '(?m)^\|\s*' + [regex]::Escape($hu) + '\s*\|.*$')
            if ($tm.Success -and $tm.Value.Contains('UnitTest/')) { $covered = $true; break }
        }
        if (-not $covered) { $missing.Add(('{0}  [{1}: sin UnitTest/ en docs/trazabilidad.md]' -f $row.Key, ($hus -join ', '))) }
    }
}
else {
    $coveragePath = Join-Path $repoRoot ('TestResults/endpoint-coverage/{0}.txt' -f $Suite)
    if (-not (Test-Path -LiteralPath $coveragePath)) {
        Write-Output ('endpoint-tests: FAIL (falta {0}; corre antes: dotnet test --project {1}/{1}.csproj)' -f $coveragePath, $Suite)
        exit 1
    }

    $hit = New-Object System.Collections.Generic.HashSet[string]
    foreach ($line in (Get-Content -LiteralPath $coveragePath)) {
        $parts = $line.Split(' ', 2)
        if ($parts.Count -eq 2) { [void]$hit.Add(('{0} {1}' -f $parts[0], (Format-Route $parts[1]))) }
    }

    foreach ($row in $rows) {
        if ($waived.ContainsKey($row.Key)) { continue }
        if (-not $hit.Contains($row.Key)) { $missing.Add($row.Key) }
    }
}

if ($missing.Count -gt 0) {
    Write-Output ('endpoint-tests: FAIL ({0}: {1} de {2} filas sin prueba)' -f $Suite, $missing.Count, $rows.Count)
    foreach ($m in $missing) { Write-Output ('  sin prueba: ' + $m) }
    exit 1
}

Write-Output ('endpoint-tests: OK ({0}: {1} filas con prueba, {2} con excepcion documentada)' -f $Suite, ($rows.Count - $waived.Count), $waived.Count)
exit 0
