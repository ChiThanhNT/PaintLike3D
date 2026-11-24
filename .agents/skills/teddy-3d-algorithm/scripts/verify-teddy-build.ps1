param(
    [string]$SolutionPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($SolutionPath)) {
    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..")
    $SolutionPath = Join-Path $repoRoot "PaintLike.sln"
}

Write-Host "=== Verifying PaintLike / Teddy 3D Build ===" -ForegroundColor Cyan
Write-Host "Solution: $SolutionPath"

dotnet build $SolutionPath -c Debug --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build FAILED with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

Write-Host "=== SUCCESS: PaintLike 3D compiled cleanly! ===" -ForegroundColor Green
