[CmdletBinding()]
param(
    [switch]$Portable
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root "Fh6Hud.slnx"
$portableProject = Join-Path $root "tests/Fh6Hud.Tests/Fh6Hud.Tests.csproj"

function Invoke-ValidationStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    Write-Host "==> $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Validation step failed: $Name"
    }
}

if ($Portable) {
    $workspace = $portableProject
    $scope = "portable telemetry"
}
else {
    $workspace = $solution
    $scope = "Windows solution"
}

Write-Host "Validating $scope"
Invoke-ValidationStep "Restore" { dotnet restore $workspace }
Invoke-ValidationStep "Whitespace formatting" {
    dotnet format $workspace whitespace --verify-no-changes --no-restore
}
Invoke-ValidationStep "Configured style formatting" {
    dotnet format $workspace style --diagnostics IDE0055 --severity error --verify-no-changes --no-restore
}
Invoke-ValidationStep "Release build (analyzers report-only)" {
    dotnet build $workspace --configuration Release --no-restore
}
Invoke-ValidationStep "Release tests" {
    dotnet test $workspace --configuration Release --no-build --no-restore
}

Write-Host "Validation passed: $scope"
