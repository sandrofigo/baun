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
