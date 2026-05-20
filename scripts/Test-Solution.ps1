$ErrorActionPreference = "Stop"

Push-Location "$PSScriptRoot\.."

try {
    dotnet test ".\SteamInputBridge.Tests\SteamInputBridge.Tests.csproj" @args
}
finally {
    Pop-Location
}
