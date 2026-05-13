param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "..\tools\PhysicalMouse.Cli\PhysicalMouse.Cli.csproj"

dotnet run --project $project -- @Arguments
