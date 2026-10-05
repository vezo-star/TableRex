<#
.SYNOPSIS
    Copies ConnectionStrings__* values from .env into .NET user-secrets shared by the Api, Worker and Migrations projects.
.DESCRIPTION
    Development only. Secrets stay outside the repository (in the user profile) and are loaded when the
    environment is Development.
#>
[CmdletBinding()]
param(
    [string]$EnvFile
)

$ErrorActionPreference = 'Stop'

if (-not $EnvFile) {
    $EnvFile = [System.IO.Path]::Combine($PSScriptRoot, '..', '.env')
}

if (-not (Test-Path $EnvFile)) {
    throw "No .env file at '$EnvFile'. Copy .env.example to .env and set local passwords first."
}

$project = [System.IO.Path]::Combine($PSScriptRoot, '..', 'Backend', 'BookingPlatform.Api', 'BookingPlatform.Api.csproj')
$keys = @('ConnectionStrings__Postgres', 'ConnectionStrings__Redis')
$values = @{}

foreach ($line in Get-Content $EnvFile) {
    if ($line -match '^\s*#' -or $line -notmatch '=') { continue }
    $name, $value = $line -split '=', 2
    if ($keys -contains $name.Trim()) { $values[$name.Trim()] = $value.Trim() }
}

foreach ($key in $keys) {
    if (-not $values.ContainsKey($key) -or $values[$key] -match 'CHANGE_ME') {
        throw "$key is missing or still contains CHANGE_ME in $EnvFile."
    }
    $secretName = $key -replace '__', ':'
    dotnet user-secrets set $secretName $values[$key] --project $project | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to set user secret $secretName." }
    Write-Host "Set user secret $secretName"
}

Write-Host 'Done. Api, Worker and Migrations share these secrets (same UserSecretsId).'
