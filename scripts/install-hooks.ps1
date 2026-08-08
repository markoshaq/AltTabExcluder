<#
.SYNOPSIS
    Installs a git pre-commit hook that checks code formatting.

.DESCRIPTION
    Copies the pre-commit script from scripts/pre-commit into .git/hooks/.
    After installation, `git commit` will run `dotnet format --verify-no-changes`
    and fail the commit if style drift is detected.

    This is opt-in — run this script once to enable the hook.

.EXAMPLE
    .\scripts\install-hooks.ps1
#>
[CmdletBinding()]
param()

$hookPath = ".git\hooks\pre-commit"
$sourcePath = "scripts\pre-commit"

if (-not (Test-Path ".git")) {
    Write-Host "::error::Not in a git repository root." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $sourcePath)) {
    Write-Host "::error::Source hook script not found: $sourcePath" -ForegroundColor Red
    exit 1
}

# Ensure the hooks directory exists.
$hookDir = ".git\hooks"
if (-not (Test-Path $hookDir)) {
    New-Item -ItemType Directory -Path $hookDir -Force | Out-Null
}

Copy-Item $sourcePath $hookPath -Force
Write-Host "Installed pre-commit hook to $hookPath" -ForegroundColor Green
Write-Host "The hook will run 'dotnet format --verify-no-changes' on every commit."
Write-Host "To remove it, delete .git\hooks\pre-commit."
