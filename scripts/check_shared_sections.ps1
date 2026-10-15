#Requires -Version 5.1
<#
.SYNOPSIS
  Verifica que las secciones compartidas sean identicas en musubi-api y musubi-app.
.DESCRIPTION
  Busca en los .md de este repo los bloques delimitados por
    <!-- Seccion compartida con musubi-app/musubi-api ... -->  ...  <!-- Fin de seccion compartida -->
  y los compara (en orden, normalizando finales de linea) con el mismo archivo del repo hermano.
  Herramienta local (no corre en el CI: cada repo es autosuficiente). Si no encuentra el otro
  repo, informa SKIP con exit 0. Compatible con PowerShell 5.1 y pwsh. ASCII puro.
.PARAMETER OtherRepoPath
  Ruta del repo hermano. Por defecto, la carpeta hermana (../musubi-app o ../musubi-api).
#>
[CmdletBinding()]
param(
    [string]$OtherRepoPath = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$repoName = Split-Path -Leaf $repoRoot

if ($OtherRepoPath -eq '') {
    $sibling = 'musubi-app'
    if ($repoName -eq 'musubi-app') { $sibling = 'musubi-api' }
    $OtherRepoPath = Join-Path (Split-Path -Parent $repoRoot) $sibling
}
if (-not (Test-Path -LiteralPath $OtherRepoPath)) {
    Write-Output ('shared: SKIP (no se encontro el repo hermano en ' + $OtherRepoPath + ')')
    exit 0
}

# Marcadores en ASCII via regex (la palabra lleva acento en los .md: "Secci.n").
$startPattern = '^<!-- Secci.n compartida con musubi-app/musubi-api'
$endPattern = '^<!-- Fin de secci.n compartida -->'

function Get-SharedBlocks([string]$file) {
    $blocks = @()
    $current = $null
    foreach ($line in [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)) {
        if ($line -match $startPattern) { $current = New-Object System.Text.StringBuilder; continue }
        if ($line -match $endPattern) {
            if ($null -ne $current) { $blocks += $current.ToString() }
            $current = $null
            continue
        }
        if ($null -ne $current) { [void]$current.Append($line.TrimEnd()).Append("`n") }
    }
    return ,$blocks
}

$failures = @()
$checked = 0
$mdFiles = Get-ChildItem -LiteralPath $repoRoot -Recurse -Filter '*.md' |
    Where-Object { $_.FullName -notmatch '[\\/](\.git|bin|obj|build|\.dart_tool)[\\/]' }

foreach ($md in $mdFiles) {
    $mine = Get-SharedBlocks $md.FullName
    if ($mine.Count -eq 0) { continue }
    $relative = $md.FullName.Substring($repoRoot.Length).TrimStart('\', '/')
    $otherFile = Join-Path $OtherRepoPath $relative
    if (-not (Test-Path -LiteralPath $otherFile)) {
        $failures += ($relative + ': no existe en el repo hermano')
        continue
    }
    $theirs = Get-SharedBlocks $otherFile
    if ($mine.Count -ne $theirs.Count) {
        $failures += ('{0}: {1} bloques aqui vs {2} en el hermano' -f $relative, $mine.Count, $theirs.Count)
        continue
    }
    for ($i = 0; $i -lt $mine.Count; $i++) {
        $checked++
        if ($mine[$i] -cne $theirs[$i]) {
            $failures += ('{0}: el bloque compartido #{1} difiere' -f $relative, ($i + 1))
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Output ('shared: FAIL ({0} diferencias)' -f $failures.Count)
    foreach ($f in $failures) { Write-Output ('  ' + $f) }
    exit 1
}
Write-Output ('shared: OK ({0} bloques compartidos identicos)' -f $checked)
exit 0
