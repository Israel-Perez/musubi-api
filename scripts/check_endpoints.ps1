#Requires -Version 5.1
<#
.SYNOPSIS
  Drift guard: docs/endpoints.md <-> codigo de Musubi.Api, en ambos sentidos.
.DESCRIPTION
  Extrae del codigo:
    - Controllers (*Controller.cs): [Route("...")] de clase + [HttpGet/Post/Put/Patch/Delete("...")].
    - Minimal APIs: MapGet/MapPost/MapPut/MapPatch/MapDelete/MapHealthChecks con literal o constante.
  Compara contra las filas "| VERBO | /ruta |" de docs/endpoints.md.
  FAIL (exit 1) si una ruta del codigo no tiene fila, o si una fila no existe en el codigo.
  Compatible con PowerShell 5.1 y pwsh. ASCII puro.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$apiDir = Join-Path $repoRoot 'Musubi.Api'
$docPath = Join-Path $repoRoot 'docs/endpoints.md'

if (-not (Test-Path -LiteralPath $docPath)) {
    Write-Output 'endpoints: FAIL (falta docs/endpoints.md)'
    exit 1
}

function Format-Route([string]$route) {
    $r = $route.Replace('v{version:apiVersion}', 'v1')
    if (-not $r.StartsWith('/')) { $r = '/' + $r }
    if ($r.Length -gt 1) { $r = $r.TrimEnd('/') }
    # Minusculas fuera de {placeholders} (respeta {id:guid}).
    $parts = [regex]::Split($r, '(\{[^}]*\})')
    for ($i = 0; $i -lt $parts.Count; $i++) {
        if (-not $parts[$i].StartsWith('{')) { $parts[$i] = $parts[$i].ToLowerInvariant() }
    }
    return (-join $parts)
}

$sources = @(Get-ChildItem -LiteralPath $apiDir -Recurse -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })

# Constantes string del proyecto (para MapHealthChecks(HealthPath) y similares).
$constants = @{}
foreach ($s in $sources) {
    $text = Get-Content -LiteralPath $s.FullName -Raw
    foreach ($m in [regex]::Matches($text, 'const\s+string\s+(\w+)\s*=\s*"([^"]*)"')) {
        $constants[$m.Groups[1].Value] = $m.Groups[2].Value
    }
}

$code = New-Object System.Collections.Generic.HashSet[string]

foreach ($s in $sources) {
    $text = Get-Content -LiteralPath $s.FullName -Raw

    if ($s.Name -like '*Controller.cs') {
        $rm = [regex]::Match($text, '\[Route\("([^"]+)"\)\]')
        $base = ''
        if ($rm.Success) { $base = $rm.Groups[1].Value }
        $token = ($s.BaseName -replace 'Controller$', '').ToLowerInvariant()
        $base = $base.Replace('[controller]', $token)
        foreach ($am in [regex]::Matches($text, '\[Http(Get|Post|Put|Patch|Delete)(?:\("([^"]*)"\))?\]')) {
            $verb = $am.Groups[1].Value.ToUpperInvariant()
            $tpl = $am.Groups[2].Value
            if ($tpl.StartsWith('~/')) { $full = $tpl.Substring(1) }
            elseif ($tpl -eq '') { $full = $base }
            else { $full = $base + '/' + $tpl }
            [void]$code.Add(('{0} {1}' -f $verb, (Format-Route $full)))
        }
    }

    foreach ($mm in [regex]::Matches($text, '\.Map(Get|Post|Put|Patch|Delete|HealthChecks)\(\s*(?:"([^"]+)"|(\w+))')) {
        $verb = $mm.Groups[1].Value.ToUpperInvariant()
        if ($verb -eq 'HEALTHCHECKS') { $verb = 'GET' }
        $route = $mm.Groups[2].Value
        if ($route -eq '') {
            $name = $mm.Groups[3].Value
            if (-not $constants.ContainsKey($name)) {
                Write-Output ("endpoints: FAIL (no se pudo resolver la constante '{0}' en {1})" -f $name, $s.Name)
                exit 1
            }
            $route = $constants[$name]
        }
        [void]$code.Add(('{0} {1}' -f $verb, (Format-Route $route)))
    }
}

$doc = New-Object System.Collections.Generic.HashSet[string]
foreach ($dm in [regex]::Matches((Get-Content -LiteralPath $docPath -Raw), '(?m)^\|\s*(GET|POST|PUT|PATCH|DELETE)\s*\|\s*(/[^\s|]*)\s*\|')) {
    [void]$doc.Add(('{0} {1}' -f $dm.Groups[1].Value, (Format-Route $dm.Groups[2].Value)))
}

$missing = @($code | Where-Object { -not $doc.Contains($_) } | Sort-Object)
$orphans = @($doc | Where-Object { -not $code.Contains($_) } | Sort-Object)

if ($missing.Count -gt 0 -or $orphans.Count -gt 0) {
    Write-Output ('endpoints: FAIL ({0} rutas sin fila, {1} filas sin ruta)' -f $missing.Count, $orphans.Count)
    foreach ($m in $missing) { Write-Output ('  falta fila en docs/endpoints.md: ' + $m) }
    foreach ($o in $orphans) { Write-Output ('  fila huerfana (no existe en el codigo): ' + $o) }
    exit 1
}
Write-Output ('endpoints: OK ({0} rutas con fila, 0 huerfanas)' -f $code.Count)
exit 0
