#!/bin/bash

# Relay Test Script
# This script helps test the Relay container auto-updater on your local machine

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DOCKER_DIR="$SCRIPT_DIR/docker"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

print_header() {
    echo -e "\n${BLUE}════════════════════════════════════════════════════════════${NC}"
    echo -e "${BLUE}  $1${NC}"
    echo -e "${BLUE}════════════════════════════════════════════════════════════${NC}\n"
}

print_success() {
    echo -e "${GREEN}✓ $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}! $1${NC}"
}

print_info() {
    echo -e "${CYAN}→ $1${NC}"
}

print_error() {
    echo -e "${RED}✗ $1${NC}"
}

# Build Relay
build() {
    print_header "Building Relay"
    cd "$DOCKER_DIR"
    docker compose build --no-cache
    print_success "Relay built successfully"
}

# Start Relay only
start_relay() {
    print_header "Starting Relay"
    cd "$DOCKER_DIR"
    docker compose up -d
    print_success "Relay started"
    echo ""
    print_info "View logs with: $0 logs"
}

# Start Relay and test containers
start_all() {
    print_header "Starting Relay and Test Containers"
    cd "$DOCKER_DIR"
    docker compose -f docker-compose.yml -f docker-compose.test.yml up -d
    print_success "All containers started"
    echo ""
    show_monitored
}

# Stop all containers
stop() {
    print_header "Stopping All Containers"
    cd "$DOCKER_DIR"
    docker compose -f docker-compose.yml -f docker-compose.test.yml down 2>/dev/null || docker compose down
    print_success "All containers stopped"
}

# Restart all containers
restart() {
    stop
    start_all
}

# Show Relay logs
logs() {
    print_header "Relay Logs (last 100 lines)"
    docker logs relay --tail 100 2>/dev/null || print_error "Relay container not running"
}

# Follow Relay logs
follow() {
    print_header "Following Relay Logs (Ctrl+C to exit)"
    docker logs relay -f 2>/dev/null || print_error "Relay container not running"
}

# Show monitored containers
show_monitored() {
    print_header "Containers with relay.enable=true"
    echo ""
    docker ps --filter "label=relay.enable=true" --format "table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Label \"relay.update\"}}" 2>/dev/null || echo "No containers found"
    echo ""
}

# Show all containers
ps() {
    print_header "All Containers"
    docker ps --format "table {{.Names}}\t{{.Image}}\t{{.Status}}"
}

# Run a complete test cycle
test() {
    print_header "Running Complete Test"
    
    echo "Step 1: Building Relay..."
    build
    
    echo ""
    echo "Step 2: Starting containers..."
    start_all
    
    echo ""
    echo "Step 3: Waiting for initial check (10 seconds)..."
    sleep 10
    
    echo ""
    echo "Step 4: Showing Relay logs..."
    logs
    
    echo ""
    print_success "Test complete!"
    echo ""
    echo "Next steps:"
    echo -e "  ${CYAN}$0 follow${NC}     - Watch logs in real-time"
    echo -e "  ${CYAN}$0 monitored${NC}  - Show monitored containers"
    echo -e "  ${CYAN}$0 stop${NC}       - Stop all containers"
}

# Quick test without rebuilding
quick() {
    print_header "Quick Test (no rebuild)"
    
    cd "$DOCKER_DIR"
    docker compose -f docker-compose.yml -f docker-compose.test.yml up -d
    
    echo ""
    print_info "Waiting 5 seconds for startup..."
    sleep 5
    
    echo ""
    show_monitored
    
    echo ""
    logs
}

# Check container status
status() {
    print_header "Relay Status"
    
    if docker ps --format '{{.Names}}' | grep -q '^relay$'; then
        print_success "Relay is running"
        echo ""
        docker ps --filter "name=relay" --format "table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}"
    else
        print_error "Relay is not running"
    fi
    
    echo ""
    print_header "Monitored Containers"
    local count=$(docker ps --filter "label=relay.enable=true" --format '{{.Names}}' 2>/dev/null | wc -l | tr -d ' ')
    echo "Found $count container(s) with relay.enable=true"
    echo ""
    docker ps --filter "label=relay.enable=true" --format "table {{.Names}}\t{{.Image}}\t{{.Label \"relay.update\"}}" 2>/dev/null
}

# Clean up everything
clean() {
    print_header "Cleaning Up"
    
    cd "$DOCKER_DIR"
    docker compose -f docker-compose.yml -f docker-compose.test.yml down -v --remove-orphans 2>/dev/null || true
    docker compose down -v --remove-orphans 2>/dev/null || true
    
    print_success "Cleanup complete"
}

# Print usage
usage() {
    echo ""
    echo -e "${CYAN}Relay Test Script${NC}"
    echo ""
    echo "Usage: $0 <command>"
    echo ""
    echo "Commands:"
    echo -e "  ${GREEN}build${NC}       Build Relay Docker image (with --no-cache)"
    echo -e "  ${GREEN}start${NC}       Start Relay only"
    echo -e "  ${GREEN}start-all${NC}   Start Relay and test containers"
    echo -e "  ${GREEN}stop${NC}        Stop all containers"
    echo -e "  ${GREEN}restart${NC}     Restart all containers"
    echo -e "  ${GREEN}logs${NC}        Show Relay logs (last 100 lines)"
    echo -e "  ${GREEN}follow${NC}      Follow Relay logs in real-time"
    echo -e "  ${GREEN}monitored${NC}   Show containers being monitored"
    echo -e "  ${GREEN}ps${NC}          Show all containers"
    echo -e "  ${GREEN}status${NC}      Show Relay and monitored container status"
    echo -e "  ${GREEN}test${NC}        Run complete test (build + start + verify)"
    echo -e "  ${GREEN}quick${NC}       Quick test without rebuilding"
    echo -e "  ${GREEN}clean${NC}       Stop and remove all containers"
    echo ""
    echo "Examples:"
    echo -e "  ${YELLOW}$0 test${NC}       # Full test cycle"
    echo -e "  ${YELLOW}$0 quick${NC}      # Quick start without rebuild"
    echo -e "  ${YELLOW}$0 follow${NC}     # Watch logs live"
    echo ""
}

# Main command handler
case "${1:-}" in
    build)
        build
        ;;
    start)
        start_relay
        ;;
    start-all)
        start_all
        ;;
    stop)
        stop
        ;;
    restart)
        restart
        ;;
    logs)
        logs
        ;;
    follow)
        follow
        ;;
    monitored)
        show_monitored
        ;;
    ps)
        ps
        ;;
    status)
        status
        ;;
    test)
        test
        ;;
    quick)
        quick
        ;;
    clean)
        clean
        ;;
    *)
        usage
        ;;
esac
