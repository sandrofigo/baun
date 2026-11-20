#Requires -Version 5.1
# Runs the baun build. Installs the .NET SDK into .dotnet if it isn't installed.
# Usage: ./build.ps1 [steps]
$ErrorActionPreference = 'Stop'

$DotnetDir = Join-Path $PSScriptRoot '.dotnet'
$DotnetChannel = '10.0'
$GlobalJson = Join-Path $PSScriptRoot 'global.json'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

# Whether the SDK that dotnet picks here, which follows global.json, is .NET 10 or later.
function Test-Sdk {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { return $false }
    $ErrorActionPreference = 'Continue' # Windows PowerShell stops on the redirected error output otherwise
    Push-Location $PSScriptRoot
    try { $version = dotnet --version 2> $null } finally { Pop-Location }
    return $LASTEXITCODE -eq 0 -and "$version" -match '^(\d+)\.' -and [int]$Matches[1] -ge 10
}

# Whether global.json sets the version of the SDK.
function Test-GlobalJsonSdk {
    if (-not (Test-Path $GlobalJson)) { return $false }
    try { return [bool](Get-Content $GlobalJson -Raw | ConvertFrom-Json).sdk.version } catch { return $false }
}

if (-not (Test-Sdk)) {
    $env:DOTNET_ROOT = $DotnetDir
    $env:PATH = "$DotnetDir$([IO.Path]::PathSeparator)$env:PATH"
    if (-not (Test-Sdk)) {
        New-Item -ItemType Directory -Force $DotnetDir | Out-Null
        $installer = Join-Path $DotnetDir 'dotnet-install.ps1'
        Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $installer -UseBasicParsing
        if (Test-GlobalJsonSdk) {
            Write-Host "Installing the .NET SDK of global.json into $DotnetDir"
            & $installer -JSonFile $GlobalJson -InstallDir $DotnetDir -NoPath
        } else {
            Write-Host "Installing the .NET $DotnetChannel SDK into $DotnetDir"
            & $installer -Channel $DotnetChannel -InstallDir $DotnetDir -NoPath
        }
        if (-not (Test-Sdk)) {
            [Console]::Error.WriteLine('The build needs the .NET SDK 10 or later, but global.json selects an older one.')
            exit 1
        }
    }
}

if (Test-Path (Join-Path $PSScriptRoot 'build/_build.csproj')) {
    dotnet run --project (Join-Path $PSScriptRoot 'build') -- @args
} else {
    dotnet run --file (Join-Path $PSScriptRoot 'Build.cs') -- @args
}
exit $LASTEXITCODE
