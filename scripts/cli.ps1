Push-Location "$PSScriptRoot\..\app"

try {
    dotnet run --project .\SteamInputBridge.csproj -- @args
}
finally {
    Pop-Location
}
