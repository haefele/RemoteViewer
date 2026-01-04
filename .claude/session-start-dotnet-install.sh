#!/bin/bash
set -e

# Use CLAUDE_ENV_FILE to persist environment variables for the session
if [ -z "$CLAUDE_ENV_FILE" ]; then
  echo "Warning: CLAUDE_ENV_FILE not set. Session environment variables may not persist."
  exit 1
fi

# Install .NET SDK dependencies (Linux)
if ! command -v dotnet &> /dev/null; then
  echo "Installing .NET SDK dependencies..."

  # Install native dependencies required for .NET
  apt-get update -qq
  apt-get install -y -qq libicu-dev ca-certificates curl wget

  # Download and install .NET 10 SDK using official script
  echo "Downloading .NET 10 SDK installer..."
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  chmod +x /tmp/dotnet-install.sh

  # Install to /opt/dotnet for cloud environment
  /tmp/dotnet-install.sh --install-dir /opt/dotnet --version latest --channel 10.0

  # Persist environment variables for the session
  echo 'export DOTNET_ROOT=/opt/dotnet' >> "$CLAUDE_ENV_FILE"
  echo 'export PATH="$PATH:/opt/dotnet"' >> "$CLAUDE_ENV_FILE"

  echo ".NET SDK installation complete"

  # Verify installation
  /opt/dotnet/dotnet --version
else
  echo ".NET SDK already installed: $(dotnet --version)"
fi

exit 0
