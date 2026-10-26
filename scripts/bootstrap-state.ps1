<#
.SYNOPSIS
  Crea (una vez) el bucket del estado de Terraform de MUSUBI: musubi-tfstate-<cuenta>.
.DESCRIPTION
  Huevo y gallina: Terraform guarda su estado en este bucket, asi que no puede crearlo el mismo.
  Idempotente: si el bucket ya existe, solo verifica versionado, cifrado y bloqueo publico.
  Requiere una sesion de AWS (aws login). Compatible con PowerShell 5.1 y pwsh. ASCII puro.
#>
[CmdletBinding()]
param(
    [string]$Region = 'us-east-1'
)

$ErrorActionPreference = 'Stop'

function Invoke-Aws {
    param([string[]]$Arguments)
    $output = & aws @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "aws $($Arguments -join ' ') fallo: $output" }
    return $output
}

$account = (Invoke-Aws @('sts', 'get-caller-identity', '--query', 'Account', '--output', 'text')).Trim()
$bucket = "musubi-tfstate-$account"

& aws s3api head-bucket --bucket $bucket 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Creando $bucket en $Region"
    Invoke-Aws @('s3api', 'create-bucket', '--bucket', $bucket, '--region', $Region) | Out-Null
} else {
    Write-Host "$bucket ya existe"
}

Invoke-Aws @('s3api', 'put-bucket-versioning', '--bucket', $bucket, '--versioning-configuration', 'Status=Enabled') | Out-Null
Invoke-Aws @('s3api', 'put-public-access-block', '--bucket', $bucket, '--public-access-block-configuration',
    'BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true') | Out-Null
Invoke-Aws @('s3api', 'put-bucket-encryption', '--bucket', $bucket, '--server-side-encryption-configuration',
    '{"Rules":[{"ApplyServerSideEncryptionByDefault":{"SSEAlgorithm":"AES256"}}]}') | Out-Null

Write-Host "OK: $bucket (versionado, cifrado, sin acceso publico)"
Write-Output $bucket
