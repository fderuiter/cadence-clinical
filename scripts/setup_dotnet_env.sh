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
        if $SUDO apt-get update -qq && $SUDO apt-get install -y --no-install-recommends dotnet-sdk-10.0; then
            SDK_INSTALLED=true
        else
            echo "  [INFO] apt install failed or dotnet-sdk-10.0 unavailable; falling back to dotnet-install.sh."
        fi
    fi

    if [ "$SDK_INSTALLED" = false ]; then
        echo "  [ACTION] Downloading official Microsoft dotnet-install script..."
        TEMP_SCRIPT="$(mktemp /tmp/dotnet-install.XXXXXX.sh)"
        curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$TEMP_SCRIPT"
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
    echo "  [INFO] PostgreSQL not reachable on port 5432 (Run 'docker compose --profile python up -d postgres' to start)."
fi

if nc -z 127.0.0.1 7687 2>/dev/null; then
    echo -e "  \033[32m[OK] Neo4j Bolt detected on port 7687.\033[0m"
else
    echo "  [INFO] Neo4j not reachable on port 7687 (Run 'docker compose --profile python up -d neo4j' to start)."
fi

echo -e "\n================================================================="
echo -e "  \033[32m.NET 10 Developer Environment Setup Complete!\033[0m"
echo -e "  Run 'dotnet build Cadence.sln' to build the solution."
echo -e "=================================================================\n"
