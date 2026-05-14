param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$solution = Join-Path $PSScriptRoot "..\virtual-mouse.slnx"

dotnet test $solution --configuration $Configuration
