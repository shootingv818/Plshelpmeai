#!/bin/bash

# IVA Scanner Docker Deployment Script
# Usage: ./deploy-docker.sh [command] [options]

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Functions
log_info() { echo -e "${BLUE}ℹ️  $1${NC}"; }
log_success() { echo -e "${GREEN}✅ $1${NC}"; }
log_warning() { echo -e "${YELLOW}⚠️  $1${NC}"; }
log_error() { echo -e "${RED}❌ $1${NC}"; }

# Configuration
COMPOSE_FILE="docker-compose.yml"
PROJECT_NAME="iva-scanner"
WORKERS_SCALE=2

# Parse command line arguments
COMMAND=${1:-"help"}
WORKERS_COUNT=${2:-$WORKERS_SCALE}

# Help function
show_help() {
    cat << EOF
IVA Scanner Docker Deployment Script

Usage: $0 [command] [options]

Commands:
  build         Build all Docker images
  up            Start all services
  down          Stop all services
  restart       Restart all services
  scale         Scale workers (usage: $0 scale <count>)
  logs          Show logs for all services
  logs-master   Show Master server logs
  logs-worker   Show Worker logs
  status        Show status of all containers
  clean         Remove all containers and volumes
  health        Check health of all services
  backup        Backup database and Redis data
  restore       Restore from backup
  update        Update and restart services
  help          Show this help

Examples:
  $0 up                 # Start all services
  $0 scale 5           # Scale to 5 workers
  $0 logs-master       # Show master logs
  $0 clean            # Clean everything

Environment Variables:
  WORKERS_COUNT=3      # Default number of workers
  DB_PASSWORD=custom   # Custom database password
EOF
}

# Check prerequisites
check_prerequisites() {
    log_info "Checking prerequisites..."
    
    if ! command -v docker &> /dev/null; then
        log_error "Docker is not installed"
        exit 1
    fi
    
    if ! command -v docker-compose &> /dev/null; then
        log_error "Docker Compose is not installed"
        exit 1
    fi
    
    if [ ! -f "$COMPOSE_FILE" ]; then
        log_error "docker-compose.yml not found"
        exit 1
    fi
    
    log_success "Prerequisites check passed"
}

# Build images
build_images() {
    log_info "Building Docker images..."
    docker-compose -p "$PROJECT_NAME" build --parallel
    log_success "Images built successfully"
}

# Start services
start_services() {
    log_info "Starting IVA Scanner services..."
    
    # Start core services first
    log_info "Starting database and Redis..."
    docker-compose -p "$PROJECT_NAME" up -d sqlserver redis
    
    # Wait for database to be ready
    log_info "Waiting for database to be ready..."
    sleep 15
    
    # Start master server
    log_info "Starting Master server..."
    docker-compose -p "$PROJECT_NAME" up -d master
    
    # Wait for master to be ready
    log_info "Waiting for Master server to be ready..."
    sleep 10
    
    # Start workers
    log_info "Starting Workers (count: $WORKERS_COUNT)..."
    docker-compose -p "$PROJECT_NAME" up -d --scale worker="$WORKERS_COUNT" worker
    
    # Start nginx
    log_info "Starting nginx load balancer..."
    docker-compose -p "$PROJECT_NAME" up -d nginx
    
    # Start optional services
    log_info "Starting management tools..."
    docker-compose -p "$PROJECT_NAME" up -d redis-commander adminer
    
    log_success "All services started successfully!"
    show_access_info
}

# Stop services
stop_services() {
    log_info "Stopping IVA Scanner services..."
    docker-compose -p "$PROJECT_NAME" down
    log_success "Services stopped"
}

# Restart services
restart_services() {
    log_info "Restarting IVA Scanner services..."
    stop_services
    start_services
}

# Scale workers
scale_workers() {
    local count=${1:-$WORKERS_SCALE}
    log_info "Scaling workers to $count instances..."
    docker-compose -p "$PROJECT_NAME" up -d --scale worker="$count" worker
    log_success "Workers scaled to $count"
}

# Show logs
show_logs() {
    local service=${1:-""}
    if [ -n "$service" ]; then
        docker-compose -p "$PROJECT_NAME" logs -f "$service"
    else
        docker-compose -p "$PROJECT_NAME" logs -f
    fi
}

# Show status
show_status() {
    log_info "IVA Scanner Service Status:"
    docker-compose -p "$PROJECT_NAME" ps
    
    echo ""
    log_info "Container Resource Usage:"
    docker stats --no-stream $(docker-compose -p "$PROJECT_NAME" ps -q)
}

# Clean everything
clean_all() {
    log_warning "This will remove all containers, images, and volumes!"
    read -p "Are you sure? (y/N): " -n 1 -r
    echo
    
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        log_info "Cleaning up..."
        docker-compose -p "$PROJECT_NAME" down -v --rmi all --remove-orphans
        log_success "Cleanup completed"
    else
        log_info "Cleanup cancelled"
    fi
}

# Health check
check_health() {
    log_info "Checking service health..."
    
    # Check Master server
    if curl -sf http://localhost:5000/health > /dev/null 2>&1; then
        log_success "Master server is healthy"
    else
        log_error "Master server is not responding"
    fi
    
    # Check nginx
    if curl -sf http://localhost/health > /dev/null 2>&1; then
        log_success "nginx is healthy"
    else
        log_error "nginx is not responding"
    fi
    
    # Check database
    if docker-compose -p "$PROJECT_NAME" exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "IvaScanner2024!" -C -Q "SELECT 1" > /dev/null 2>&1; then
        log_success "Database is healthy"
    else
        log_error "Database connection failed"
    fi
    
    # Check Redis
    if docker-compose -p "$PROJECT_NAME" exec -T redis redis-cli -a "IvaRedis2024!" ping | grep -q PONG; then
        log_success "Redis is healthy"
    else
        log_error "Redis connection failed"
    fi
}

# Backup data
backup_data() {
    local backup_dir="./backups/$(date +%Y%m%d_%H%M%S)"
    mkdir -p "$backup_dir"
    
    log_info "Creating backup in $backup_dir..."
    
    # Backup database
    docker-compose -p "$PROJECT_NAME" exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
        -S localhost -U sa -P "IvaScanner2024!" -C \
        -Q "BACKUP DATABASE IvaScanner TO DISK = '/tmp/backup.bak'" || true
    
    docker cp "$(docker-compose -p "$PROJECT_NAME" ps -q sqlserver):/tmp/backup.bak" "$backup_dir/database.bak"
    
    # Backup Redis
    docker-compose -p "$PROJECT_NAME" exec -T redis redis-cli -a "IvaRedis2024!" --rdb /tmp/dump.rdb
    docker cp "$(docker-compose -p "$PROJECT_NAME" ps -q redis):/tmp/dump.rdb" "$backup_dir/redis.rdb"
    
    log_success "Backup completed: $backup_dir"
}

# Update services
update_services() {
    log_info "Updating IVA Scanner services..."
    
    # Pull latest images
    docker-compose -p "$PROJECT_NAME" pull
    
    # Rebuild custom images
    build_images
    
    # Restart services
    restart_services
    
    log_success "Update completed"
}

# Show access information
show_access_info() {
    echo ""
    log_success "🚀 IVA Scanner is now running!"
    echo ""
    echo "📋 Access URLs:"
    echo "   Main Dashboard:    http://localhost"
    echo "   Direct Master:     http://localhost:5000"  
    echo "   Redis Commander:   http://localhost:8081"
    echo "   SQL Server Admin:  http://localhost:8080"
    echo ""
    echo "🔧 Management Commands:"
    echo "   View logs:         $0 logs"
    echo "   Scale workers:     $0 scale <count>"
    echo "   Check health:      $0 health"
    echo "   Stop services:     $0 down"
    echo ""
}

# Main command handler
case $COMMAND in
    "build")
        check_prerequisites
        build_images
        ;;
    "up"|"start")
        check_prerequisites
        build_images
        start_services
        ;;
    "down"|"stop")
        stop_services
        ;;
    "restart")
        restart_services
        ;;
    "scale")
        scale_workers "$WORKERS_COUNT"
        ;;
    "logs")
        show_logs
        ;;
    "logs-master")
        show_logs "master"
        ;;
    "logs-worker")
        show_logs "worker"
        ;;
    "logs-nginx")
        show_logs "nginx"
        ;;
    "status"|"ps")
        show_status
        ;;
    "clean")
        clean_all
        ;;
    "health")
        check_health
        ;;
    "backup")
        backup_data
        ;;
    "update")
        update_services
        ;;
    "help"|"--help"|"-h")
        show_help
        ;;
    *)
        log_error "Unknown command: $COMMAND"
        echo ""
        show_help
        exit 1
        ;;
esac