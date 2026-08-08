<#
.SYNOPSIS
    Checks code style without making changes (CI-equivalent check).

.DESCRIPTION
    Runs `dotnet format --verify-no-changes` on the entire solution.
    Exits non-zero if any files would be reformatted. This is the same
    check CI runs — use it locally to catch format drift before pushing.

.EXAMPLE
    .\scripts\format-check.ps1
#>
[CmdletBinding()]
param()

Write-Host "Checking formatting (no changes will be made)..."
dotnet format AltTabExcluder.sln --verify-no-changes --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "::error::Format check failed — run scripts\format.ps1 to auto-fix." -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "Format check passed — no changes needed." -ForegroundColor Green
