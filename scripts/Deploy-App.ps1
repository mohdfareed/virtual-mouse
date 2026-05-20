param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $Output = "$PSScriptRoot\..\bin"
)

$ErrorActionPreference = "Stop"

$appProject = Resolve-Path "$PSScriptRoot\..\SteamInputBridge.App\SteamInputBridge.App.csproj"
$outputPath = [System.IO.Path]::GetFullPath($Output)

if (Test-Path $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

dotnet publish $appProject `
    --configuration $Configuration `
    --runtime $Runtime `
    --output $outputPath `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishDocumentationFile=false `
    -p:DebugType=embedded

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Deployed SteamInputBridge to $outputPath"
