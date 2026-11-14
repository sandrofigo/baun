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
