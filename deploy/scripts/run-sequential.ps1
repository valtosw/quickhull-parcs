param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z][a-z0-9-]{4,28}[a-z0-9]$')]
    [string] $ProjectId,

    [int] $PointCount = 500000,

    [int] $Seed = 42,

    [int] $Warmup = 1,

    [int] $Repetitions = 5,

    [string] $Region = 'europe-central2',

    [string] $Repository = 'parcs',

    [string] $ImageTag = 'lab'
)

$ErrorActionPreference = 'Stop'

if ($PointCount -lt 0 -or $Warmup -lt 0 -or $Repetitions -lt 1)
{
    throw 'PointCount and Warmup must be non-negative, and Repetitions must be at least 1.'
}

$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$TemplatePath = Join-Path $ProjectRoot 'deploy/jobs/seq-job.yaml'
$Registry = "$Region-docker.pkg.dev/$ProjectId/$Repository"
$JobName = "quickhull-seq-$PointCount-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$TemporaryManifest = Join-Path ([System.IO.Path]::GetTempPath()) "$JobName.yaml"

try
{
    $Manifest = Get-Content -LiteralPath $TemplatePath -Raw
    $Manifest = $Manifest.Replace('__REGISTRY__', $Registry).Replace('__IMAGE_TAG__', $ImageTag).Replace('__POINT_COUNT__', $PointCount.ToString()).Replace('__SEED__', $Seed.ToString()).Replace('__WARMUP__', $Warmup.ToString()).Replace('__REPETITIONS__', $Repetitions.ToString()).Replace('__JOB_NAME__', $JobName)
    Set-Content -LiteralPath $TemporaryManifest -Value $Manifest -Encoding utf8

    & kubectl apply --filename $TemporaryManifest

    if ($LASTEXITCODE -ne 0)
    {
        throw "Could not create Kubernetes Job $JobName."
    }

    & kubectl wait '--for=condition=complete' "job/$JobName" --timeout=30m

    if ($LASTEXITCODE -ne 0)
    {
        throw "Kubernetes Job $JobName did not complete."
    }

    & kubectl logs "job/$JobName"

    if ($LASTEXITCODE -ne 0)
    {
        throw "Could not read logs for Kubernetes Job $JobName."
    }
}
finally
{
    if (Test-Path -LiteralPath $TemporaryManifest)
    {
        Remove-Item -LiteralPath $TemporaryManifest -Force
    }
}
