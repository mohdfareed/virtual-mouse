param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$solution = Join-Path $PSScriptRoot "..\virtual-mouse.slnx"

dotnet format $solution
dotnet build $solution --configuration $Configuration
