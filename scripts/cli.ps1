param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "..\cli\Cli.csproj"

dotnet run --project $project -- @Arguments
