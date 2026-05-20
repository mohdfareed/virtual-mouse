$ErrorActionPreference = "Stop"

Push-Location "$PSScriptRoot\.."

try {
    dotnet format ".\SteamInputBridge.slnx"
    dotnet build ".\SteamInputBridge.slnx" -- @args
}
finally {
    Pop-Location
}
