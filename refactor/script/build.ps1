Push-Location "$PSScriptRoot\..\apps\Tray"
try {
    dotnet build .\Tray.csproj -- @args
}
finally {
    Pop-Location
}

Push-Location "$PSScriptRoot\..\apps\Cli"
try {
    dotnet build .\Cli.csproj -- @args
}
finally {
    Pop-Location
}
