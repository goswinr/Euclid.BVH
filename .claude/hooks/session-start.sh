#!/bin/bash
# SessionStart hook for Claude Code on the web.
# Installs the .NET SDK and restores the packages this repository needs, so that
# `dotnet build`, `dotnet run --project Test/Test.fsproj` and the Fable JS tests
# work in a cloud session.
#
# It runs asynchronously: the session starts right away and this keeps going in
# the background. See the note on the race window at the bottom of this file.
set -euo pipefail

echo '{"async": true, "asyncTimeout": 900000}'

# Only cloud sessions need this. A local machine has its own SDK.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

PROJECT_DIR="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
DOTNET_INSTALL_DIR="$HOME/.dotnet"

# Matches the 'dotnet-version: 10.x' of .github/workflows/build.yml and test.yml.
# Test/Test.fsproj targets net10.0, the library targets net6.0 and net472.
DOTNET_CHANNEL="10.0"

# Export the environment first, before the slow download below, so that the
# session picks it up as early as possible.
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export DOTNET_ROOT=\"$DOTNET_INSTALL_DIR\""
    echo "export PATH=\"$DOTNET_INSTALL_DIR:$DOTNET_INSTALL_DIR/tools:\$PATH\""
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    echo 'export DOTNET_NOLOGO=1'
    echo 'export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1'
  } >> "$CLAUDE_ENV_FILE"
fi

export DOTNET_ROOT="$DOTNET_INSTALL_DIR"
export PATH="$DOTNET_INSTALL_DIR:$DOTNET_INSTALL_DIR/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# Idempotent: a matching SDK that is already there is reused, so a resumed or
# cached container skips the download. A half written install fails this check
# and is redone.
if [ -x "$DOTNET_INSTALL_DIR/dotnet" ] \
  && "$DOTNET_INSTALL_DIR/dotnet" --list-sdks 2>/dev/null | grep -q "^${DOTNET_CHANNEL%%.*}\."; then
  echo "SessionStart: .NET SDK $("$DOTNET_INSTALL_DIR/dotnet" --version) is already installed."
else
  echo "SessionStart: installing the .NET SDK $DOTNET_CHANNEL ..."
  install_script="$(mktemp)"
  curl -fsSL -o "$install_script" https://dot.net/v1/dotnet-install.sh
  bash "$install_script" --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_INSTALL_DIR" --no-path
  rm -f "$install_script"
  echo "SessionStart: installed the .NET SDK $("$DOTNET_INSTALL_DIR/dotnet" --version)."
fi

cd "$PROJECT_DIR"

# NuGet packages for the library and the test project.
echo "SessionStart: restoring NuGet packages ..."
dotnet restore

# The local tools of .config/dotnet-tools.json: fable (the JS and TS tests) and fsdocs.
echo "SessionStart: restoring .NET local tools ..."
dotnet tool restore

# mocha and typescript for the Fable tests. `npm install` rather than `npm ci`
# so that a cached node_modules is reused instead of being wiped.
if command -v npm > /dev/null 2>&1; then
  echo "SessionStart: installing npm packages for the Fable tests ..."
  npm install --prefix "$PROJECT_DIR/Test"
else
  echo "SessionStart: npm is not available, skipping the Fable test dependencies."
fi

echo "SessionStart: ready. dotnet build and dotnet run --project Test/Test.fsproj will work now."

# Note on the async race window: until this finishes, `dotnet` is not on PATH
# yet. If a session needs to build in its very first seconds, either wait for
# this hook or drop the async line above to make the hook synchronous.
