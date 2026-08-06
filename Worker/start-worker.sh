#!/bin/bash

# IVA Scanner Worker Startup Script
# Usage: ./start-worker.sh [options]

set -e

# Default values
ENVIRONMENT="Production"
CONFIG_FILE="appsettings.json"
LOG_LEVEL="Information"
WORKER_ID=""
MASTER_URL=""

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -e|--environment)
            ENVIRONMENT="$2"
            shift 2
            ;;
        -c|--config)
            CONFIG_FILE="$2"
            shift 2
            ;;
        -l|--log-level)
            LOG_LEVEL="$2"
            shift 2
            ;;
        -i|--worker-id)
            WORKER_ID="$2"
            shift 2
            ;;
        -m|--master-url)
            MASTER_URL="$2"
            shift 2
            ;;
        -h|--help)
            echo "Usage: $0 [options]"
            echo "Options:"
            echo "  -e, --environment    Environment (Development/Production)"
            echo "  -c, --config         Configuration file path"
            echo "  -l, --log-level      Log level (Debug/Information/Warning/Error)"
            echo "  -i, --worker-id      Worker ID"
            echo "  -m, --master-url     Master server URL"
            echo "  -h, --help          Show this help"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

echo "🚀 Starting IVA Scanner Worker..."
echo "Environment: $ENVIRONMENT"
echo "Config: $CONFIG_FILE"
echo "Log Level: $LOG_LEVEL"

# Check if dotnet is installed
if ! command -v dotnet &> /dev/null; then
    echo "❌ .NET is not installed. Please install .NET 8.0 Runtime"
    exit 1
fi

# Check if config file exists
if [[ ! -f "$CONFIG_FILE" ]]; then
    echo "❌ Configuration file not found: $CONFIG_FILE"
    exit 1
fi

# Create logs directory
mkdir -p logs

# Set environment variables
export ASPNETCORE_ENVIRONMENT="$ENVIRONMENT"
export DOTNET_ENVIRONMENT="$ENVIRONMENT"

if [[ -n "$WORKER_ID" ]]; then
    export IVASCANNER_WORKER_ID="$WORKER_ID"
fi

if [[ -n "$MASTER_URL" ]]; then
    export IVASCANNER_MASTER_URL="$MASTER_URL"
fi

# Function to handle cleanup on exit
cleanup() {
    echo "🛑 Shutting down worker..."
    if [[ -n "$WORKER_PID" ]]; then
        kill -TERM "$WORKER_PID" 2>/dev/null || true
        wait "$WORKER_PID" 2>/dev/null || true
    fi
    echo "✅ Worker stopped"
}

# Set trap for cleanup
trap cleanup EXIT INT TERM

# Start the worker
echo "▶️ Starting worker process..."
dotnet run --configuration Release --verbosity quiet &
WORKER_PID=$!

echo "✅ Worker started with PID: $WORKER_PID"
echo "📋 Worker ID: ${WORKER_ID:-$(hostname)-$(date +%s)}"
echo "🔗 Master URL: ${MASTER_URL:-'from config'}"
echo ""
echo "Press Ctrl+C to stop the worker"

# Wait for the worker process
wait "$WORKER_PID"