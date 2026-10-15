#Requires -Version 5.1
<#
.SYNOPSIS
  critic-guardrails (musubi-api): checklist ligero pre-push y job `critic` del CI.
.DESCRIPTION
  Bloqueantes (exit 1):
    B1 marcador TODO en codigo/config (usar NOTE (NN-NN) o NOTE (HU-NN)).
    B2 secretos: password|secret en DTOs (waiver por linea "critic-allow: input-credential (HU-NN)")
       y connection strings con password en appsettings versionados.
    B3 *Controller.cs sin [Authorize...] ni [AllowAnonymous].
    B4 catch en *Controller.cs (el manejo de errores vive en el pipeline, 01-03).
  Avisos (no bloquean): NOTE sin ID, waivers usados.
  Ambito: diff contra origin/main + cambios locales + untracked. Si no existe origin/main
  (repo nuevo), analiza todos los archivos versionados + untracked.
  Compatible con PowerShell 5.1 y pwsh (sin ?. / ?? / -Parallel). ASCII puro.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)

$failures = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]

# powershell-quirks: git escribe en stderr (p. ej. "From https://...") y con EAP=Stop eso es
# terminating aunque haya 2>$null. Todo git pasa por aqui: 2>&1 + filtrar ErrorRecord.
function Invoke-Git {
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        return @(git @args 2>&1 | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" })
    } finally {
        $ErrorActionPreference = $prev
    }
}

function Test-OriginMain {
    $remotes = Invoke-Git remote
    if ($remotes -contains 'origin') {
        Invoke-Git fetch origin main | Out-Null
    }
    $null = Invoke-Git rev-parse --verify --quiet 'origin/main'
    return ($LASTEXITCODE -eq 0)
}

$hasBase = Test-OriginMain

function Get-ScopeFiles {
    $all = @()
    if ($hasBase) {
        $all += Invoke-Git diff --name-only 'origin/main...HEAD'
        $all += Invoke-Git diff --name-only HEAD
    } else {
        $all += Invoke-Git ls-files
    }
    $all += Invoke-Git ls-files --others --exclude-standard
    return @($all | Where-Object { $_ -and $_.Trim() -ne '' } | Select-Object -Unique)
}

function Get-AddedLines([string]$file) {
    if (-not $hasBase) { return @(Get-Content -LiteralPath $file) }
    $null = Invoke-Git ls-files --error-unmatch -- $file
    if ($LASTEXITCODE -ne 0) { return @(Get-Content -LiteralPath $file) }
    $diff = Invoke-Git diff -U0 'origin/main...HEAD' -- $file
    $diff += Invoke-Git diff -U0 HEAD -- $file
    return @($diff | Where-Object { $_ -match '^\+' -and $_ -notmatch '^\+\+\+' })
}

$files = @(Get-ScopeFiles | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
if ($files.Count -eq 0) {
    Write-Host 'critic: sin archivos en el ambito - PASS'
    exit 0
}

# Archivos que definen los propios guardarrailes (contienen los patrones que buscan).
$selfDefinitions = @('scripts/critic-guardrails.ps1', '.semgrep/semgrep.yaml', '.claude/hooks/readonly-git-guard.ps1')
$codeExt = @('.cs', '.ps1', '.psm1', '.yml', '.yaml', '.json', '.csproj', '.props', '.targets', '.slnx', '.config', '.sh')

foreach ($f in $files) {
    $path = $f -replace '\\', '/'
    if ($selfDefinitions -contains $path) { continue }
    $ext = [System.IO.Path]::GetExtension($path).ToLowerInvariant()
    $isTest = $path -match '^(UnitTest|IntegrationTest|SecurityTest|ContractTest|DatabaseTest)/'
    $lines = @(Get-Content -LiteralPath $f)

    # B1: TODO en comentario (mayusculas; S1135 ya cubre minusculas en .cs).
    if ($codeExt -contains $ext) {
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -cmatch '(//|#|/\*|<!--).*\bTODO\b') {
                $failures.Add("${path}:$($i + 1) B1 marcador TODO (usar NOTE (NN-NN) o NOTE (HU-NN))")
            }
        }
    }

    # B2a: password|secret en DTOs de produccion, salvo waiver explicito por linea.
    if ($ext -eq '.cs' -and -not $isTest -and ($path -match '(?i)Dtos?\.cs$' -or $path -match '(?i)/Dtos?/')) {
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '(?i)password|secret') {
                if ($lines[$i] -match 'critic-allow:\s*input-credential\s*\(HU-\d{2}\)') {
                    $warnings.Add("${path}:$($i + 1) waiver input-credential (revisar que sea DTO de entrada)")
                } else {
                    $failures.Add("${path}:$($i + 1) B2 posible secreto en DTO")
                }
            }
        }
    }

    # B2b: connection string con password en appsettings versionados.
    if ($path -match '(?i)appsettings[^/]*\.json$' -and $path -notmatch '(?i)\.Local\.json$') {
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '(?i)(password|pwd)\s*=\s*[^;"\s]+') {
                $failures.Add("${path}:$($i + 1) B2 connection string con password en archivo versionado")
            }
        }
    }

    if ($path -match 'Controller\.cs$' -and -not $isTest) {
        # B3: auth explicita.
        $content = [string]::Join("`n", $lines)
        if ($content -notmatch '\[Authorize(\]|\()' -and $content -notmatch '\[AllowAnonymous\]') {
            $failures.Add("${path}:1 B3 controller sin [Authorize] ni [AllowAnonymous]")
        }
        # B4: catch en controllers.
        foreach ($line in (Get-AddedLines $f)) {
            if ($line -match '\bcatch\b') {
                $failures.Add("${path} B4 catch en Controller (el manejo de errores vive en el pipeline, 01-03)")
                break
            }
        }
    }

    # A1: NOTE sin ID (formatos validos: NOTE (NN-NN) o NOTE (HU-NN)).
    if ($ext -eq '.cs') {
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -cmatch '\bNOTE\b' -and $lines[$i] -notmatch 'NOTE\s*\((\d{2}-\d{2}|HU-\d{2})\)') {
                $warnings.Add("${path}:$($i + 1) A1 NOTE sin ID de spec duena")
            }
        }
    }
}

foreach ($w in $warnings) { Write-Host "WARN $w" }
$scope = 'diff vs origin/main'
if (-not $hasBase) { $scope = 'repo completo (sin origin/main)' }
if ($failures.Count -gt 0) {
    foreach ($e in $failures) { Write-Host "FAIL $e" }
    Write-Host ("critic: FAIL ({0} bloqueantes, {1} avisos; ambito: {2})" -f $failures.Count, $warnings.Count, $scope)
    exit 1
}
Write-Host ("critic: PASS ({0} archivos analizados, {1} avisos; ambito: {2})" -f $files.Count, $warnings.Count, $scope)
exit 0
