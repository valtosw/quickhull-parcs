$ErrorActionPreference = 'Stop'

$ExistingSecret = & kubectl get secret parcs-database --ignore-not-found --output=name

if ($LASTEXITCODE -ne 0)
{
    throw 'Could not check whether the parcs-database Secret exists.'
}

if ($ExistingSecret)
{
    Write-Output 'The parcs-database Secret already exists.'
}
else
{
    $RandomBytes = [byte[]]::new(32)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($RandomBytes)
    $Password = [Convert]::ToBase64String($RandomBytes)
    $EncodedPassword = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($Password))
    $SecretManifest = @"
apiVersion: v1
kind: Secret
metadata:
  name: parcs-database
type: Opaque
data:
  password: $EncodedPassword
"@

    $SecretManifest | & kubectl apply --filename -

    if ($LASTEXITCODE -ne 0)
    {
        throw 'Could not create the parcs-database Secret.'
    }

    Write-Output 'Created the parcs-database Secret with a generated password.'
}
