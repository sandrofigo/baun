namespace Baun.Cli;

/// <summary>
/// The bootstrap scripts <c>baun init</c> creates next to a build, so it runs on machines without the .NET SDK.
/// </summary>
/// <remarks>
/// The scripts use an installed .NET 10 (or later) SDK when there is one. Otherwise they install the latest .NET 10
/// SDK into a <c>.dotnet</c> folder next to them with Microsoft's <c>dotnet-install</c> script, and reuse it on later
/// runs. Then they run the build with <c>dotnet run</c>, passing all arguments through.
/// </remarks>
internal static class BootstrapScripts
{
    /// <summary>The folder the scripts install the SDK into. It belongs in <c>.gitignore</c>.</summary>
    public const string DotnetFolder = ".dotnet/";

    /// <summary>The script for Linux and macOS, which must be executable.</summary>
    public const string ShellScriptName = "build.sh";

    /// <summary>
    /// Gets the scripts to create in <paramref name="directory"/>, keyed by path.
    /// </summary>
    public static Dictionary<string, string> Create(string directory) => new()
    {
        [Path.Combine(directory, ShellScriptName)] = ShellScript,
        [Path.Combine(directory, "build.ps1")] = PowerShellScript,
        [Path.Combine(directory, "build.cmd")] = CmdScript,
    };

    private const string ShellScript = """
        #!/usr/bin/env sh
        # Runs the baun build. Installs the .NET SDK into .dotnet if it isn't installed.
        # Usage: ./build.sh [steps]
        set -eu

        SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)
        DOTNET_CHANNEL="10.0"
        export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

        has_sdk() {
            command -v dotnet > /dev/null 2>&1 &&
                dotnet --list-sdks | grep -Eq '^([1-9][0-9])\.'
        }

        if ! has_sdk; then
            DOTNET_DIR="$SCRIPT_DIR/.dotnet"
            if [ ! -x "$DOTNET_DIR/dotnet" ]; then
                echo "Installing the .NET $DOTNET_CHANNEL SDK into $DOTNET_DIR"
                mkdir -p "$DOTNET_DIR"
                curl -sSfL https://dot.net/v1/dotnet-install.sh -o "$DOTNET_DIR/dotnet-install.sh"
                sh "$DOTNET_DIR/dotnet-install.sh" --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_DIR" --no-path
            fi
            export DOTNET_ROOT="$DOTNET_DIR" PATH="$DOTNET_DIR:$PATH"
        fi

        if [ -f "$SCRIPT_DIR/build/Build.csproj" ]; then
            exec dotnet run --project "$SCRIPT_DIR/build" -- "$@"
        else
            exec dotnet run --file "$SCRIPT_DIR/Build.cs" -- "$@"
        fi

        """;

    private const string PowerShellScript = """
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

        if (Test-Path (Join-Path $PSScriptRoot 'build/Build.csproj')) {
            dotnet run --project (Join-Path $PSScriptRoot 'build') -- @args
        } else {
            dotnet run --file (Join-Path $PSScriptRoot 'Build.cs') -- @args
        }
        exit $LASTEXITCODE

        """;

    // Lets Windows users run "build Test" without calling PowerShell themselves.
    private const string CmdScript = "@powershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0build.ps1\" %*\r\n";
}
