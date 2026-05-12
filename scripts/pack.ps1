param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$solution = Join-Path $PSScriptRoot "..\virtual-mouse.slnx"
$output = Join-Path $PSScriptRoot "..\artifacts\packages"

dotnet pack $solution --configuration $Configuration --output $output
