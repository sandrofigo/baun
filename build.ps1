#Requires -Version 5.1
# Runs the baun build. Installs the .NET SDK into .dotnet if it isn't installed.
# Usage: ./build.ps1 [steps]
$ErrorActionPreference = 'Stop'

$DotnetChannel = '10.0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Test-Sdk {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { return $false }
    return [bool](dotnet --list-sdks | Where-Object { [int]($_.Split('.')[0]) -ge 10 })
}

if (-not (Test-Sdk)) {
    $dotnetDir = Join-Path $PSScriptRoot '.dotnet'
    $dotnetExe = Join-Path $dotnetDir $(if ($IsLinux -or $IsMacOS) { 'dotnet' } else { 'dotnet.exe' })
    if (-not (Test-Path $dotnetExe)) {
        Write-Host "Installing the .NET $DotnetChannel SDK into $dotnetDir"
        New-Item -ItemType Directory -Force $dotnetDir | Out-Null
        $installer = Join-Path $dotnetDir 'dotnet-install.ps1'
        Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $installer -UseBasicParsing
        & $installer -Channel $DotnetChannel -InstallDir $dotnetDir -NoPath
    }
    $env:DOTNET_ROOT = $dotnetDir
    $env:PATH = "$dotnetDir$([IO.Path]::PathSeparator)$env:PATH"
}

if (Test-Path (Join-Path $PSScriptRoot 'build/_build.csproj')) {
    dotnet run --project (Join-Path $PSScriptRoot 'build') -- @args
} else {
    dotnet run --file (Join-Path $PSScriptRoot 'Build.cs') -- @args
}
exit $LASTEXITCODE
