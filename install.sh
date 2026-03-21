#!/usr/bin/env bash
set -euo pipefail

REPO="HannibalLade/kommit"

# Detect OS
case "$(uname -s)" in
    Darwin) OS="osx" ;;
    Linux)  OS="linux" ;;
    *)      echo "Unsupported OS: $(uname -s)" >&2; exit 1 ;;
esac

# Detect architecture
case "$(uname -m)" in
    arm64|aarch64) ARCH="arm64" ;;
    x86_64)        ARCH="x64" ;;
    *)             echo "Unsupported architecture: $(uname -m)" >&2; exit 1 ;;
esac

RID="${OS}-${ARCH}"
ASSET_NAME="kommit-${RID}"

echo "Detecting platform: ${RID}"

# Get latest release download URL
RELEASE_URL="https://api.github.com/repos/${REPO}/releases/latest"
DOWNLOAD_URL=$(curl -fsSL "$RELEASE_URL" | grep -o "\"browser_download_url\": *\"[^\"]*${ASSET_NAME}\"" | head -1 | cut -d'"' -f4)

if [ -z "$DOWNLOAD_URL" ]; then
    echo "Error: No binary found for your platform (${RID})." >&2
    echo "Check https://github.com/${REPO}/releases for available downloads." >&2
    exit 1
fi

# Determine install directory
if [ -w /usr/local/bin ]; then
    INSTALL_DIR="/usr/local/bin"
elif [ -d "$HOME/.local/bin" ]; then
    INSTALL_DIR="$HOME/.local/bin"
else
    mkdir -p "$HOME/.local/bin"
    INSTALL_DIR="$HOME/.local/bin"
fi

INSTALL_PATH="${INSTALL_DIR}/kommit"

echo "Downloading kommit for ${RID}..."
curl -fsSL -o "$INSTALL_PATH" "$DOWNLOAD_URL"
chmod +x "$INSTALL_PATH"

echo "Installed kommit to ${INSTALL_PATH}"

# Check if install dir is in PATH
if ! echo "$PATH" | tr ':' '\n' | grep -qx "$INSTALL_DIR"; then
    echo ""
    echo "NOTE: ${INSTALL_DIR} is not in your PATH."
    echo "Add it by running:"
    echo "  export PATH=\"${INSTALL_DIR}:\$PATH\""
    echo "Then add that line to your ~/.zshrc or ~/.bashrc."
fi

echo ""

# First-time setup
CONFIG_FILE="$HOME/.kommitconfig"
if [ ! -f "$CONFIG_FILE" ]; then
    echo "Let's configure kommit!"
    echo ""

    # Auto-add
    printf "Automatically stage all files when none are staged? [y/N] "
    read -r AUTO_ADD
    case "$AUTO_ADD" in y|Y) AUTO_ADD="true" ;; *) AUTO_ADD="false" ;; esac

    # Auto-push
    printf "Automatically push after each commit? [y/N] "
    read -r AUTO_PUSH
    case "$AUTO_PUSH" in y|Y) AUTO_PUSH="true" ;; *) AUTO_PUSH="false" ;; esac

    # Auto-pull
    printf "Automatically pull before each commit? [y/N] "
    read -r AUTO_PULL
    case "$AUTO_PULL" in y|Y) AUTO_PULL="true" ;; *) AUTO_PULL="false" ;; esac

    cat > "$CONFIG_FILE" <<EOF
{
  "autoAdd": ${AUTO_ADD},
  "autoPush": ${AUTO_PUSH},
  "autoPull": ${AUTO_PULL},
  "pullStrategy": "rebase",
  "pushStrategy": "simple",
  "defaultScope": null,
  "maxCommitLength": 72,
  "maxStagedFiles": null,
  "maxStagedLines": null,
  "apiToken": null
}
EOF

    echo ""
    echo "Config saved to ~/.kommitconfig"
    echo "You can change these anytime with 'kommit config'."
else
    echo "Existing config found at ~/.kommitconfig"
fi

echo ""
echo "Run 'kommit --version' to verify."
