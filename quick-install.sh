#!/bin/bash

# IVA Scanner Quick Install Script
# Usage: curl -fsSL https://raw.githubusercontent.com/shootingv818/Plshelpmeai/main/quick-install.sh | bash

set -e

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

print_status() {
    echo -e "${GREEN}✅ $1${NC}"
}

print_info() {
    echo -e "${BLUE}ℹ️  $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

print_error() {
    echo -e "${RED}❌ $1${NC}"
}

print_info "🚀 IVA Scanner Quick Install Starting..."

# Check if running as root
if [[ $EUID -eq 0 ]]; then
   print_error "Don't run this script as root! Use a regular user with sudo access."
   exit 1
fi

# Detect OS
if [[ "$OSTYPE" == "linux-gnu"* ]]; then
    if [ -f /etc/debian_version ]; then
        OS="debian"
        print_info "Detected: Debian/Ubuntu"
    elif [ -f /etc/redhat-release ]; then
        OS="redhat"
        print_info "Detected: RedHat/CentOS"
    else
        print_warning "Unknown Linux distribution, assuming Debian-like"
        OS="debian"
    fi
else
    print_error "This script only supports Linux"
    exit 1
fi

# Install dependencies
print_info "📦 Installing dependencies..."

if [ "$OS" == "debian" ]; then
    sudo apt update
    sudo apt install -y curl wget git sqlite3 unzip
    
    # Install .NET 8
    if ! command -v dotnet &> /dev/null; then
        print_info "Installing .NET 8..."
        wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
        sudo dpkg -i /tmp/packages-microsoft-prod.deb
        sudo apt update
        sudo apt install -y aspnetcore-runtime-8.0 dotnet-sdk-8.0
    fi
    
    # Install Redis
    sudo apt install -y redis-server
    sudo systemctl enable redis-server
    sudo systemctl start redis-server
    
elif [ "$OS" == "redhat" ]; then
    sudo yum update -y
    sudo yum install -y curl wget git sqlite unzip
    
    # Install .NET 8
    if ! command -v dotnet &> /dev/null; then
        sudo rpm -Uvh https://packages.microsoft.com/config/centos/7/packages-microsoft-prod.rpm
        sudo yum install -y aspnetcore-runtime-8.0 dotnet-sdk-8.0
    fi
    
    # Install Redis
    sudo yum install -y redis
    sudo systemctl enable redis
    sudo systemctl start redis
fi

print_status "Dependencies installed"

# Create installation directory
INSTALL_DIR="/opt/iva-scanner"
print_info "📁 Creating installation directory: $INSTALL_DIR"
sudo mkdir -p $INSTALL_DIR
sudo chown $USER:$USER $INSTALL_DIR

# Clone or download project
print_info "⬇️ Downloading IVA Scanner..."
cd /tmp

# Try git clone first, fallback to wget
if command -v git &> /dev/null; then
    git clone https://github.com/shootingv818/Plshelpmeai.git iva-scanner-src
    cp -r iva-scanner-src/* $INSTALL_DIR/
else
    # Fallback: download as zip (you'll need to upload to GitHub first)
    print_warning "Git not available, trying direct download..."
    wget -O iva-scanner.zip "https://github.com/shootingv818/Plshelpmeai/archive/main.zip"
    unzip -q iva-scanner.zip
    cp -r Plshelpmeai-main/* $INSTALL_DIR/
fi

print_status "Project downloaded"

# Build project
print_info "🔨 Building project..."
cd $INSTALL_DIR/Master

# Create production config
cat > appsettings.Production.json << 'EOF'
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=../iva-scanner.db",
    "Redis": "localhost:6379"
  },
  "Master": {
    "PublicUrl": "http://localhost:5000"
  },
  "Worker": {
    "HeartbeatTimeout": "00:02:00",
    "TaskLeaseTimeout": "00:02:00",
    "DefaultApiKey": "demo-api-key-$(date +%s)"
  },
  "TaskDistribution": {
    "ChunkSize": 100,
    "MaxQueuedTasks": 1000
  }
}
EOF

# Build project
dotnet restore
dotnet build -c Release

print_status "Project built successfully"

# Run database migrations
print_info "🗄️ Setting up database..."
dotnet ef database update --no-build

print_status "Database initialized"

# Create systemd service
print_info "⚙️ Creating system service..."
sudo tee /etc/systemd/system/iva-scanner.service > /dev/null << EOF
[Unit]
Description=IVA Scanner Master Server
After=network.target redis-server.service
Wants=network.target
Requires=redis-server.service

[Service]
Type=notify
User=$USER
Group=$USER
WorkingDirectory=$INSTALL_DIR/Master
ExecStart=/usr/bin/dotnet run --configuration Release --no-build
Restart=always
RestartSec=10
KillSignal=SIGINT
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:5000
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

# Logs
StandardOutput=journal
StandardError=journal
SyslogIdentifier=iva-scanner

[Install]
WantedBy=multi-user.target
EOF

# Enable and start service
sudo systemctl daemon-reload
sudo systemctl enable iva-scanner
sudo systemctl start iva-scanner

print_status "Service installed and started"

# Wait for service to start
print_info "⏳ Waiting for service to start..."
sleep 5

# Check service status
if systemctl is-active --quiet iva-scanner; then
    print_status "Service is running successfully!"
else
    print_error "Service failed to start. Checking logs..."
    sudo journalctl -u iva-scanner --no-pager -n 20
    exit 1
fi

# Get server IP
SERVER_IP=$(curl -s ifconfig.me 2>/dev/null || curl -s ipinfo.io/ip 2>/dev/null || hostname -I | awk '{print $1}')

# Final instructions
echo ""
echo "🎉 IVA Scanner installed successfully!"
echo "=================================================="
echo ""
echo "📋 Access Information:"
echo "   🌐 Web Dashboard: http://$SERVER_IP:5000"
echo "   🏠 Local Access:  http://localhost:5000"
echo ""
echo "🔧 Service Management:"
echo "   ▶️  Start:   sudo systemctl start iva-scanner"
echo "   ⏹️  Stop:    sudo systemctl stop iva-scanner"
echo "   🔄 Restart: sudo systemctl restart iva-scanner"
echo "   📊 Status:  sudo systemctl status iva-scanner"
echo "   📋 Logs:    sudo journalctl -u iva-scanner -f"
echo ""
echo "📁 Installation Directory: $INSTALL_DIR"
echo "⚙️  Configuration: $INSTALL_DIR/Master/appsettings.Production.json"
echo ""
echo "🚀 Next Steps:"
echo "   1. Open http://$SERVER_IP:5000 in your browser"
echo "   2. Go to 'سرورهای Remote' to add worker servers"
echo "   3. Add IVA accounts in 'اکانت‌های ایوا'"
echo "   4. Start scanning cards! 🎯"
echo ""

# Optional: Open firewall port
if command -v ufw &> /dev/null; then
    if sudo ufw status | grep -q "Status: active"; then
        print_warning "Firewall is active. Opening port 5000..."
        sudo ufw allow 5000/tcp
        print_status "Port 5000 opened"
    fi
fi

print_status "Installation complete! 🚀"

# Quick health check
print_info "🔍 Running health check..."
sleep 2

if curl -s http://localhost:5000/health > /dev/null 2>&1; then
    print_status "Health check passed! Service is responding."
else
    print_warning "Health check failed. Service might still be starting..."
    print_info "Try: curl http://localhost:5000"
fi

echo ""
echo "💡 Pro tip: Access your dashboard at: http://$SERVER_IP:5000"
echo "📞 Need help? Check the logs: sudo journalctl -u iva-scanner -f"