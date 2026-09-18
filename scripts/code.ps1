# Resolve parent directory from 'scripts' folder (repository root)
$repoRoot = Resolve-Path "$PSScriptRoot\.."
$projectId = $env:IPID
if ([string]::IsNullOrWhiteSpace($projectId)) {
    Write-Error "The environment variable 'IPID' isnt defined at `$PROFILE."
    exit 1
}

Write-Host "Starting VS Code w/ Infisical at repository root: $repoRoot" -ForegroundColor Green

# Runs infisical injecting the environment and opening VS Code at repository root directory
infisical run --env=dev --projectId=$projectId -- code $repoRoot