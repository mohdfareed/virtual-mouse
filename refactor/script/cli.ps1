Push-Location "$PSScriptRoot\..\apps\Cli"
try {
    dotnet run --project .\Cli.csproj -- @args
}
finally {
    Pop-Location
}
