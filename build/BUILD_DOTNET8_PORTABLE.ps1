$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

function Pause-End { Write-Host ""; Read-Host "Press Enter to close" }

try {
    Write-Host ""
    Write-Host "=== ADB USB Speed Test - .NET 8 self-contained build ==="
    Write-Host ""

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw ".NET 8 SDK wurde nicht gefunden. Installiere das .NET 8 SDK auf dem BUILD-PC."
    }

    dotnet --version


    if (-not (Test-Path "..\src\ADB_USB_Speed_Test\ADB_USB_Speed_Test.ico")) {
        throw "ADB_USB_Speed_Test.ico wurde nicht gefunden."
    }

    if (-not (Test-Path ".\adb\adb.exe")) {
        throw "Kopiere den kompletten Inhalt deines funktionierenden D:\adb\ Ordners nach .\adb\"
    }

    Remove-Item ".\publish" -Recurse -Force -ErrorAction SilentlyContinue

    dotnet publish "..\src\ADB_USB_Speed_Test\ADB_USB_Speed_Test.csproj" `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o ".\publish"

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

    Copy-Item ".\adb" ".\publish\adb" -Recurse -Force

    Write-Host ""
    Write-Host "BUILD SUCCESSFUL" -ForegroundColor Green
    Write-Host ""
    Write-Host "Portable app:"
    Write-Host "  .\publish\ADB_USB_Speed_Test.exe"
    Write-Host ""
    Write-Host "No Python and no .NET runtime are required on the target PC."
}
catch {
    Write-Host ""
    Write-Host "BUILD FAILED" -ForegroundColor Red
    Write-Host $_ -ForegroundColor Red
}
finally {
    Pause-End
}
