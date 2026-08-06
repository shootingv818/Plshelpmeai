#!/bin/bash

# IVA Scanner Worker Service Installation Script
# Run as root: sudo ./install-service.sh

set -e

if [[ $EUID -ne 0 ]]; then
   echo "❌ This script must be run as root (use sudo)"
   exit 1
fi

INSTALL_DIR="/opt/iva-scanner/worker"
SERVICE_NAME="iva-worker"
SERVICE_USER="iva-worker"
SERVICE_GROUP="iva-worker"

echo "🔧 Installing IVA Scanner Worker Service..."

# Create service user and group
if ! getent group "$SERVICE_GROUP" > /dev/null 2>&1; then
    echo "➕ Creating group: $SERVICE_GROUP"
    groupadd --system "$SERVICE_GROUP"
fi

if ! getent passwd "$SERVICE_USER" > /dev/null 2>&1; then
    echo "➕ Creating user: $SERVICE_USER"
    useradd --system --group "$SERVICE_GROUP" --home-dir "$INSTALL_DIR" \
            --shell /usr/sbin/nologin --comment "IVA Scanner Worker Service" \
            "$SERVICE_USER"
fi

# Create installation directory
echo "📁 Creating installation directory: $INSTALL_DIR"
mkdir -p "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR/logs"
mkdir -p "$INSTALL_DIR/temp"

# Build and publish the application
echo "🔨 Building application..."
dotnet publish -c Release -o "$INSTALL_DIR" --self-contained false

# Copy configuration files
echo "📋 Setting up configuration..."
cp appsettings.json "$INSTALL_DIR/"
cp appsettings.Production.json "$INSTALL_DIR/" 2>/dev/null || echo "No Production config found"

# Create default configuration if it doesn't exist
if [[ ! -f "$INSTALL_DIR/appsettings.Production.json" ]]; then
    cat > "$INSTALL_DIR/appsettings.Production.json" << EOF
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  },
  "Master": {
    "BaseUrl": "http://localhost:5000"
  },
  "Worker": {
    "LogLevel": "Information"
  }
}
EOF
fi

# Set permissions
echo "🔐 Setting permissions..."
chown -R "$SERVICE_USER:$SERVICE_GROUP" "$INSTALL_DIR"
chmod 755 "$INSTALL_DIR"
chmod 644 "$INSTALL_DIR"/*.json
chmod 755 "$INSTALL_DIR/IvaScanner.Worker"

# Install systemd service
echo "⚙️ Installing systemd service..."
cp iva-worker.service /etc/systemd/system/

# Update service file paths
sed -i "s|/opt/iva-scanner/worker|$INSTALL_DIR|g" /etc/systemd/system/iva-worker.service

# Reload systemd and enable service
systemctl daemon-reload
systemctl enable "$SERVICE_NAME"

echo "✅ Installation completed successfully!"
echo ""
echo "📋 Next steps:"
echo "1. Edit configuration: $INSTALL_DIR/appsettings.Production.json"
echo "2. Start service: sudo systemctl start $SERVICE_NAME"
echo "3. Check status: sudo systemctl status $SERVICE_NAME"
echo "4. View logs: sudo journalctl -u $SERVICE_NAME -f"
echo ""
echo "🔧 Service management commands:"
echo "  Start:   sudo systemctl start $SERVICE_NAME"
echo "  Stop:    sudo systemctl stop $SERVICE_NAME"
echo "  Restart: sudo systemctl restart $SERVICE_NAME"
echo "  Status:  sudo systemctl status $SERVICE_NAME"
echo "  Logs:    sudo journalctl -u $SERVICE_NAME -f"