# Publish VeloxAPI as a standalone or framework-dependent release
param (
    [string]$Configuration = "Release",
    [string]$OutputDir = "$PSScriptRoot/../Releases/VeloxAPI"
)

Write-Host "Publishing VeloxAPI to $OutputDir..." -ForegroundColor Cyan
dotnet publish "$PSScriptRoot/../VeloxAPI/VeloxAPI.csproj" -c $Configuration -o $OutputDir
if ($LASTEXITCODE -eq 0) {
    Write-Host "VeloxAPI published successfully." -ForegroundColor Green
} else {
    Write-Error "VeloxAPI publication failed."
}
