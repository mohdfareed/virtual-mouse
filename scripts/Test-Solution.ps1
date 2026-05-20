$ErrorActionPreference = "Stop"

Push-Location "$PSScriptRoot\.."

try {
    dotnet test ".\tests\SteamInputBridge.Tests.csproj" @args
}
finally {
    Pop-Location
}
