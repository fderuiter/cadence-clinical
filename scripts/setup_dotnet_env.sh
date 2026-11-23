#!/usr/bin/env bash
# ==============================================================================
# Cadence Clinical Platform - Automated .NET 10 Developer Environment Setup (Linux/macOS)
# Requirements: PRD-SYS-053, ADR-2233
# ==============================================================================

set -euo pipefail

TARGET_CHANNEL="${1:-10.0}"

echo -e "\n================================================================="
echo -e "  Cadence Clinical Research Software — .NET 10 Environment Setup "
echo -e "=================================================================\n"

# 1. Check or install .NET 10 SDK
echo "[1/5] Checking .NET SDK Installation..."
SDK_INSTALLED=false

if command -v dotnet &>/dev/null; then
    CURRENT_VERSION=$(dotnet --version 2>/dev/null || true)
    if [[ "$CURRENT_VERSION" =~ ^10\. ]]; then
        echo -e "  \033[32m[OK] Found active .NET 10 SDK: $CURRENT_VERSION\033[0m"
        SDK_INSTALLED=true
    else
        echo "  [INFO] Detected existing .NET SDK ($CURRENT_VERSION), but .NET 10 is required."
    fi
fi

if [ "$SDK_INSTALLED" = false ]; then
    SUDO=""
    if [ "$(id -u)" -ne 0 ]; then SUDO="sudo"; fi

    if command -v apt-get &>/dev/null && [ -f /etc/debian_version ]; then
        # .NET 10 GA is packaged in Ubuntu 24.04+/Debian 13+ apt feeds; prefer it
        # since it's faster and stays patched via the normal OS update path.
        echo "  [ACTION] Installing dotnet-sdk-10.0 via apt..."
        if $SUDO apt-get -o Acquire::http::Timeout="3" -o Acquire::https::Timeout="3" update -qq && $SUDO apt-get install -y --no-install-recommends dotnet-sdk-10.0; then
            SDK_INSTALLED=true
        else
            echo "  [INFO] apt install failed or dotnet-sdk-10.0 unavailable; falling back to dotnet-install.sh."
        fi
    fi

    if [ "$SDK_INSTALLED" = false ]; then
        echo "  [ACTION] Downloading official Microsoft dotnet-install script..."
        TEMP_SCRIPT="$(mktemp /tmp/dotnet-install.XXXXXX.sh)"
        curl -fsSL --connect-timeout 15 --retry 3 https://dot.net/v1/dotnet-install.sh -o "$TEMP_SCRIPT"

        PINNED_DOTNET_INSTALL_SH_SHA256="082f7685e156738a1b2e2ed8381a621870d4ce8e8c59278034556f05c186eb2e" # pragma: allowlist secret
        EXPECTED_HASH="${DOTNET_INSTALL_SH_SHA256:-$PINNED_DOTNET_INSTALL_SH_SHA256}"

        echo "  [ACTION] Verifying SHA256 checksum for dotnet-install.sh..."
        if command -v sha256sum &>/dev/null; then
            ACTUAL_HASH=$(sha256sum "$TEMP_SCRIPT" | awk '{print $1}')
        elif command -v shasum &>/dev/null; then
            ACTUAL_HASH=$(shasum -a 256 "$TEMP_SCRIPT" | awk '{print $1}')
        else
            echo -e "  \033[31m[ERROR] Neither sha256sum nor shasum is available to verify installer integrity.\033[0m" >&2
            rm -f "$TEMP_SCRIPT"
            exit 1
        fi

        EXPECTED_HASH_LOWER="$(echo "$EXPECTED_HASH" | tr '[:upper:]' '[:lower:]')"
        ACTUAL_HASH_LOWER="$(echo "$ACTUAL_HASH" | tr '[:upper:]' '[:lower:]')"

        if [ "$ACTUAL_HASH_LOWER" != "$EXPECTED_HASH_LOWER" ]; then
            echo -e "  \033[31m[ERROR] Checksum verification failed for dotnet-install.sh!\033[0m" >&2
            echo -e "    Expected: $EXPECTED_HASH_LOWER" >&2
            echo -e "    Actual:   $ACTUAL_HASH_LOWER" >&2
            echo -e "    To override, set environment variable DOTNET_INSTALL_SH_SHA256.\033[0m" >&2
            rm -f "$TEMP_SCRIPT"
            exit 1
        fi

        echo -e "  \033[32m[OK] SHA256 checksum verified ($ACTUAL_HASH_LOWER)\033[0m"
        chmod +x "$TEMP_SCRIPT"

        INSTALL_DIR="$HOME/.dotnet"
        echo "  [ACTION] Installing .NET $TARGET_CHANNEL SDK (GA) to $INSTALL_DIR..."
        "$TEMP_SCRIPT" --channel "$TARGET_CHANNEL" --install-dir "$INSTALL_DIR"
        rm -f "$TEMP_SCRIPT"

        export DOTNET_ROOT="$INSTALL_DIR"
        export PATH="$INSTALL_DIR:$PATH"
    fi

    NEW_VERSION=$(dotnet --version 2>/dev/null || true)
    echo -e "  \033[32m[OK] .NET 10 SDK successfully installed: $NEW_VERSION\033[0m"
fi

# 2. Install essential global dotnet tools
echo -e "\n[2/5] Checking & Installing Global .NET Tools..."
TOOLS=("dotnet-ef" "dotnet-format")
for tool in "${TOOLS[@]}"; do
    echo "  Installing/Updating $tool..."
    dotnet tool update --global "$tool" 2>/dev/null || dotnet tool install --global "$tool" 2>/dev/null || true
    echo -e "  \033[32m[OK] $tool is configured.\033[0m"
done

mkdir -p "$HOME/.dotnet/tools"
cat << "EOF" > "$HOME/.dotnet/tools/dotnet-cadence"
#!/usr/bin/env bash
REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null || echo "/app/cadence-clinical")"
CLI_PROJECT="$REPO_ROOT/packages/cli/src/Cadence.Cli.csproj"
exec dotnet run --project "$CLI_PROJECT" -- "$@"
EOF
chmod +x "$HOME/.dotnet/tools/dotnet-cadence"
echo -e "  \033[32m[OK] dotnet cadence CLI wrapper is configured.\033[0m"

# 3. Development HTTPS certificates
echo -e "\n[3/5] Configuring HTTPS Development Certificates..."
dotnet dev-certs https --clean || true
dotnet dev-certs https --trust || true
echo -e "  \033[32m[OK] Development HTTPS certificate configured.\033[0m"

# 4. Environment Configuration (.env)
echo -e "\n[4/5] Checking Environment Configuration (.env)..."
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [ ! -f "$REPO_ROOT/.env" ]; then
    if [ -f "$REPO_ROOT/.env.example" ]; then
        cp "$REPO_ROOT/.env.example" "$REPO_ROOT/.env"
        echo -e "  \033[32m[OK] Created default .env from .env.example.\033[0m"
    fi
else
    echo -e "  \033[32m[OK] Existing .env file detected.\033[0m"
fi

# 5. Database Connectivity Checks (Docker / Local)
echo -e "\n[5/5] Checking Local Clinical Datastores..."
if nc -z 127.0.0.1 5432 2>/dev/null; then
    echo -e "  \033[32m[OK] PostgreSQL detected on port 5432.\033[0m"
else
    echo "  [INFO] PostgreSQL not reachable on port 5432 (Run 'docker compose -f docker/docker-compose.yml up -d postgres' to start)."
fi


echo -e "\n================================================================="
echo -e "  \033[32m.NET 10 Developer Environment Setup Complete!\033[0m"
echo -e "  Run 'dotnet build Cadence.slnx' to build the solution."
echo -e "=================================================================\n"
