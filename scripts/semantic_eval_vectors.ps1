# HU-60: saca los vectores de Gemini del conjunto de evaluacion de las coincidencias.
#
# Uso (la llave nunca se imprime ni se guarda):
#   $env:GEMINI_API_KEY = '...'; pwsh scripts/semantic_eval_vectors.ps1
#
# Lee UnitTest/Matching/Fixtures/semantic-eval.json y escribe semantic-eval-vectors.json junto a el, con el mismo
# modelo, tipos de tarea y dimensiones que GeminiTextEmbedder: RETRIEVAL_DOCUMENT para las publicaciones y
# RETRIEVAL_QUERY para las solicitudes. Los textos son ejemplos del campus inventados.

$ErrorActionPreference = 'Stop'
$model = 'gemini-embedding-001'
$dimensions = 256
$endpoint = "https://generativelanguage.googleapis.com/v1beta/models/$($model):batchEmbedContents"
$folder = Join-Path $PSScriptRoot '..' 'UnitTest' 'Matching' 'Fixtures'

$key = $env:GEMINI_API_KEY
if ([string]::IsNullOrWhiteSpace($key)) {
    throw 'falta GEMINI_API_KEY'
}

function Get-Vectors([string[]] $Texts, [string] $Task) {
    $body = @{
        requests = @($Texts | ForEach-Object {
            @{ model = "models/$model"; content = @{ parts = @(@{ text = $_ }) }; taskType = $Task; outputDimensionality = $dimensions }
        })
    } | ConvertTo-Json -Depth 6 -Compress
    $reply = Invoke-RestMethod -Method Post -Uri $endpoint -ContentType 'application/json; charset=utf-8' `
        -Headers @{ 'x-goog-api-key' = $key } -Body ([Text.Encoding]::UTF8.GetBytes($body))
    return ,@($reply.embeddings | ForEach-Object { , @($_.values | ForEach-Object { [math]::Round([double]$_, 5) }) })
}

$data = Get-Content (Join-Path $folder 'semantic-eval.json') -Raw -Encoding utf8 | ConvertFrom-Json
$products = Get-Vectors ($data.products | ForEach-Object text) 'RETRIEVAL_DOCUMENT'
$requests = Get-Vectors ($data.requests | ForEach-Object title) 'RETRIEVAL_QUERY'

$vectors = [ordered]@{}
for ($i = 0; $i -lt $data.products.Count; $i++) { $vectors[$data.products[$i].id] = $products[$i] }
for ($i = 0; $i -lt $data.requests.Count; $i++) { $vectors[$data.requests[$i].id] = $requests[$i] }

$out = [ordered]@{
    model      = $model
    dimensions = $dimensions
    taskTypes  = [ordered]@{ products = 'RETRIEVAL_DOCUMENT'; requests = 'RETRIEVAL_QUERY' }
    vectors    = $vectors
}
$out | ConvertTo-Json -Depth 5 -Compress | Set-Content (Join-Path $folder 'semantic-eval-vectors.json') -Encoding utf8NoBOM -NoNewline
Write-Output "vectores: $($vectors.Count) de $dimensions dimensiones"
