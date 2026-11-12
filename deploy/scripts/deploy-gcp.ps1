param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z][a-z0-9-]{4,28}[a-z0-9]$')]
    [string] $ProjectId,

    [string] $Region = 'europe-central2',

    [string] $Repository = 'parcs',

    [string] $ImageTag = 'lab'
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$SourceDeployDirectory = Join-Path $ProjectRoot 'deploy'
$Registry = "$Region-docker.pkg.dev/$ProjectId/$Repository"
$TemporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "quickhull-deploy-$([guid]::NewGuid().ToString('N'))"
$NormalizedTemporaryRoot = [System.IO.Path]::GetFullPath($TemporaryRoot)
$NormalizedTemporaryBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())

if (-not $NormalizedTemporaryRoot.StartsWith($NormalizedTemporaryBase, [System.StringComparison]::OrdinalIgnoreCase))
{
    throw 'The generated deployment directory is outside the system temporary directory.'
}

try
{
    New-Item -ItemType Directory -Path $TemporaryRoot | Out-Null
    Copy-Item -LiteralPath $SourceDeployDirectory -Destination $TemporaryRoot -Recurse

    $KustomizationPath = Join-Path $TemporaryRoot 'deploy/gcp/kustomization.yaml'
    $Kustomization = Get-Content -LiteralPath $KustomizationPath -Raw
    $Kustomization = $Kustomization.Replace('__REGISTRY__', $Registry).Replace('__IMAGE_TAG__', $ImageTag)
    Set-Content -LiteralPath $KustomizationPath -Value $Kustomization -Encoding utf8

    & (Join-Path $PSScriptRoot 'ensure-database-secret.ps1')
    & kubectl apply --kustomize (Join-Path $TemporaryRoot 'deploy/gcp')

    if ($LASTEXITCODE -ne 0)
    {
        throw 'Could not apply the PARCS GKE deployment.'
    }

    foreach ($Deployment in @('parcs-daemon', 'parcs-hostapi', 'parcs-portal', 'parcs-database'))
    {
        & kubectl rollout status "deployment/$Deployment" --timeout=10m

        if ($LASTEXITCODE -ne 0)
        {
            throw "Deployment $Deployment did not become ready."
        }
    }
}
finally
{
    $ResolvedTemporaryRoot = [System.IO.Path]::GetFullPath($TemporaryRoot)

    if ($ResolvedTemporaryRoot.StartsWith($NormalizedTemporaryBase, [System.StringComparison]::OrdinalIgnoreCase))
    {
        Remove-Item -LiteralPath $ResolvedTemporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
