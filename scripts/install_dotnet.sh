#!/bin/bash

# Only run in remote (web) environments
if [ "$CLAUDE_CODE_REMOTE" != "true" ]; then
  exit 0
fi

# Check if dotnet is already installed and working
if command -v dotnet &> /dev/null && dotnet --version &> /dev/null; then
  echo ".NET SDK is already installed: $(dotnet --version)"
  exit 0
fi

echo "Installing .NET SDK..."

# Download and run the official .NET install script
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh

# Install .NET 10.0 (as required by this project)
/tmp/dotnet-install.sh --channel 10.0

# Add dotnet to PATH for this session
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools"

# Verify installation
if dotnet --version &> /dev/null; then
  echo ".NET SDK installed successfully: $(dotnet --version)"
else
  echo "Warning: .NET SDK installation may have failed"
fi

# Clean up
rm -f /tmp/dotnet-install.sh

exit 0
