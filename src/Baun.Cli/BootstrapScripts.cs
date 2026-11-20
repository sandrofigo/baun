namespace Baun.Cli;

/// <summary>
/// The bootstrap scripts <c>baun init</c> creates next to a build, so it runs on machines without the .NET SDK.
/// </summary>
/// <remarks>
/// The scripts use an installed SDK when the one <c>dotnet</c> picks for their folder, which follows a
/// <c>global.json</c>, is .NET 10 or later. Otherwise they install an SDK into a <c>.dotnet</c> folder next to them
/// with Microsoft's <c>dotnet-install</c> script, and reuse it on later runs: the version that <c>global.json</c> sets,
/// or the latest .NET 10 SDK. Then they run the build with <c>dotnet run</c>, passing all arguments through.
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
    public static Dictionary<AbsolutePath, string> Create(AbsolutePath directory) => new()
    {
        [directory / ShellScriptName] = ShellScript.ReplaceLineEndings("\n"),
        [directory / "build.ps1"] = PowerShellScript.ReplaceLineEndings("\n"),
        [directory / "build.cmd"] = CmdScript
    };

    private const string ShellScript = """
        #!/usr/bin/env sh
        # Runs the baun build. Installs the .NET SDK into .dotnet if it isn't installed.
        # Usage: ./build.sh [steps]
        set -eu

        SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)
        DOTNET_DIR="$SCRIPT_DIR/.dotnet"
        DOTNET_CHANNEL="10.0"
        export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

        # Whether the SDK that dotnet picks here, which follows global.json, is .NET 10 or later.
        has_sdk() {
            version=$(cd "$SCRIPT_DIR" && dotnet --version 2> /dev/null) || return 1
            [ "${version%%.*}" -ge 10 ] 2> /dev/null
        }

        # Whether global.json sets the version of the SDK.
        has_global_json_sdk() {
            [ -f "$SCRIPT_DIR/global.json" ] &&
                tr -d '\r' < "$SCRIPT_DIR/global.json" | awk '/"sdk"/,/}/' | grep -q '"version"'
        }

        if ! has_sdk; then
            export DOTNET_ROOT="$DOTNET_DIR" PATH="$DOTNET_DIR:$PATH"
            if ! has_sdk; then
                mkdir -p "$DOTNET_DIR"
                curl -sSfL https://dot.net/v1/dotnet-install.sh -o "$DOTNET_DIR/dotnet-install.sh"
                if has_global_json_sdk; then
                    echo "Installing the .NET SDK of global.json into $DOTNET_DIR"
                    sh "$DOTNET_DIR/dotnet-install.sh" --jsonfile "$SCRIPT_DIR/global.json" --install-dir "$DOTNET_DIR" --no-path
                else
                    echo "Installing the .NET $DOTNET_CHANNEL SDK into $DOTNET_DIR"
                    sh "$DOTNET_DIR/dotnet-install.sh" --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_DIR" --no-path
                fi
                if ! has_sdk; then
                    echo "The build needs the .NET SDK 10 or later, but global.json selects an older one." >&2
                    exit 1
                fi
            fi
        fi

        if [ -f "$SCRIPT_DIR/build/_build.csproj" ]; then
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

        """;

    // Lets Windows users run "./build.cmd Test" without calling PowerShell themselves.
    private const string CmdScript =
        "@powershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0build.ps1\" %*\r\n";
}
