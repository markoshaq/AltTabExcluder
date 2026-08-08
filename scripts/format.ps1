<#
.SYNOPSIS
    Runs dotnet format to auto-fix code style issues.

.DESCRIPTION
    Runs `dotnet format` on the entire solution, applying style fixes
    defined in .editorconfig. Run this before committing to avoid CI
    format-check failures.

.EXAMPLE
    .\scripts\format.ps1
#>
[CmdletBinding()]
param()

Write-Host "Running dotnet format (auto-fix)..."
dotnet format AltTabExcluder.sln
if ($LASTEXITCODE -ne 0) {
    Write-Host "::error::dotnet format failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "Format complete." -ForegroundColor Green
