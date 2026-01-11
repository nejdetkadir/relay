# Relay

Relay is a Docker container auto-updater that monitors running containers and automatically updates them when newer images are available. It works similar to [Watchtower](https://github.com/containrrr/watchtower) but with enhanced features like rolling updates, semantic versioning support, and zero-downtime deployments.

## Features

- **Automatic Updates**: Detects when newer container images are available and automatically updates running containers
- **Rolling Updates**: Zero-downtime updates with health check verification (enabled by default)
- **Label-Based Filtering**: Only monitors containers with `relay.enable=true` label
- **Versioned Tag Support**: Supports semantic versioning - can update to newer patch, minor, or major versions
- **Digest-Based Updates**: Detects when the same tag has been rebuilt with new content
- **Private Registry Support**: Reads credentials from `~/.docker/config.json` for private registries
- **Configuration Preservation**: Preserves container settings (volumes, networks, environment variables, ports) during updates
- **Health Check Integration**: Uses Docker HEALTHCHECK or verifies container stability before switching
- **Interval-Based Checking**: Configurable check interval (default: 5 minutes)
- **Structured Logging**: Clear, informative logs for monitoring and debugging
- **Docker Socket Access**: Runs as a Docker container with access to the Docker socket

## Table of Contents

- [Quick Start](#quick-start)
- [Configuration](#configuration)
- [Update Strategies](#update-strategies)
- [Rolling Updates](#rolling-updates)
- [Container Labels](#container-labels)
- [Private Registry Support](#private-registry-support)
- [Usage Examples](#usage-examples)
- [How It Works](#how-it-works)
- [Logging](#logging)
- [Building from Source](#building-from-source)
- [Testing](#testing)
- [Troubleshooting](#troubleshooting)
- [Architecture](#architecture)

## Quick Start

### Using Docker Compose

1. **Build and run Relay**:

```bash
cd docker
docker compose up -d
```

2. **Add the `relay.enable=true` label** to containers you want to monitor:

```yaml
services:
  myapp:
    image: myapp:latest
    labels:
      - relay.enable=true
```

### Using Docker Run

```bash
docker run -d \
  --name relay \
  --restart unless-stopped \
  -v /var/run/docker.sock:/var/run/docker.sock:ro \
  -v ~/.docker/config.json:/root/.docker/config.json:ro \
  -e RELAY_CHECK_INTERVAL_SECONDS=300 \
  relay:latest
```

## Configuration

### Environment Variables

Relay is configured via environment variables. All configuration options have sensible defaults.

| Variable | Default | Description |
|----------|---------|-------------|
| `RELAY_CHECK_INTERVAL_SECONDS` | `300` | Interval between update checks (in seconds). Set to `60` for 1-minute checks, `600` for 10-minute checks, etc. |
| `RELAY_LABEL_KEY` | `relay.enable` | Label key used to identify monitored containers. Change this if you want to use a different label name. |
| `RELAY_CLEANUP_OLD_IMAGES` | `false` | Remove old images after updating containers. Set to `true` to automatically clean up unused images. |
| `RELAY_DOCKER_HOST` | `unix:///var/run/docker.sock` | Docker daemon socket path. Use `tcp://host:port` for remote Docker hosts. |
| `RELAY_DOCKER_TIMEOUT_SECONDS` | `60` | Timeout in seconds for Docker API operations. Increase for slow networks or large images. |
| `RELAY_CHECK_ON_STARTUP` | `true` | Run an immediate check when Relay starts. Set to `false` to wait for the first interval. |
| `RELAY_DOCKER_CONFIG_PATH` | (auto-detected) | Path to Docker config.json for registry auth. Auto-detects from `DOCKER_CONFIG`, `~/.docker/config.json`, or `/root/.docker/config.json`. |
| `RELAY_ROLLING_UPDATE_ENABLED` | `true` | Enable rolling updates with health checks. Set to `false` to use legacy stop-then-start updates. |
| `RELAY_HEALTH_CHECK_TIMEOUT_SECONDS` | `60` | Maximum time to wait for container health check during rolling updates (in seconds). |
| `RELAY_HEALTH_CHECK_INTERVAL_SECONDS` | `5` | Interval between health check polls during rolling updates (in seconds). |

### Configuration via docker-compose.yml

```yaml
services:
  relay:
    image: relay:latest
    container_name: relay
    restart: unless-stopped
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - ~/.docker/config.json:/root/.docker/config.json:ro
    environment:
      - RELAY_CHECK_INTERVAL_SECONDS=300
      - RELAY_CLEANUP_OLD_IMAGES=false
      - RELAY_ROLLING_UPDATE_ENABLED=true
      - RELAY_HEALTH_CHECK_TIMEOUT_SECONDS=60
    labels:
      - relay.enable=false  # Don't monitor Relay itself
```

## Update Strategies

Relay supports different update strategies configured via the `relay.update` label. Each strategy determines which new versions are acceptable for updates.

| Strategy | Behavior | Example | Use Case |
|----------|----------|---------|----------|
| `digest` | Only update if the same tag has a new digest (image rebuilt) | `nginx:1.25` rebuilt → update | Production services using fixed tags |
| `patch` | Update to newer patch versions only | `1.25.0` → `1.25.1` ✓, `1.26.0` ✗ | Stable services needing security patches |
| `minor` | Update to newer minor versions (includes patch) | `1.25.0` → `1.26.0` ✓, `2.0.0` ✗ | Services that can accept new features |
| `major` | Update to any newer version | `1.25.0` → `2.0.0` ✓ | Experimental or development services |

### Strategy Examples

```yaml
services:
  # Digest strategy (default) - updates when nginx:1.25 is rebuilt
  web:
    image: nginx:1.25
    labels:
      - relay.enable=true
      # Uses default 'digest' strategy

  # Patch strategy - updates to any 1.25.x version
  api:
    image: myapp:1.25.0
    labels:
      - relay.enable=true
      - relay.update=patch

  # Minor strategy - updates to any 1.x.x version
  worker:
    image: myapp:1.25.0
    labels:
      - relay.enable=true
      - relay.update=minor

  # Major strategy - updates to any newer version
  experimental:
    image: myapp:1.25.0
    labels:
      - relay.enable=true
      - relay.update=major
```

### Version Parsing

Relay supports various version tag formats:

- **Standard semver**: `1.25.0`, `2.0.0-beta.1`
- **With prefixes**: `v1.25.0`, `version-1.25.0`, `release-2.0.0`
- **Partial versions**: `1.25` (treated as `1.25.0`), `3` (treated as `3.0.0`)
- **Non-version tags**: `latest`, `stable`, `edge`, `dev`, `nightly` (only work with `digest` strategy)

## Rolling Updates

Relay supports **zero-downtime rolling updates** by default. This feature ensures minimal service interruption during container updates.

### How Rolling Updates Work

1. **Staging Container**: Creates a temporary container (`{name}-relay-staging`) with the new image **without port bindings** to avoid conflicts
2. **Health Verification**: Waits for the staging container to become healthy:
   - If Docker HEALTHCHECK is defined: waits for `healthy` status
   - If no HEALTHCHECK: verifies container stays running for 5 seconds
3. **Switchover**: Only after health check passes:
   - Stops the old container
   - Removes the old container
   - Removes the staging container
   - Creates the final container with full configuration (including ports)
4. **Rollback**: If health check fails, the staging container is removed and the old container continues running

### Benefits

- **Zero Downtime**: Old container keeps serving traffic until new one is verified
- **Automatic Rollback**: Failed updates don't affect running services
- **Health Verification**: Ensures new containers are working before switching
- **No Port Conflicts**: Staging container runs without ports to avoid binding issues

### Disabling Rolling Updates

To use the legacy stop-then-start approach:

```yaml
services:
  relay:
    environment:
      - RELAY_ROLLING_UPDATE_ENABLED=false
```

### Health Check Configuration

#### Global Health Check Settings

```yaml
services:
  relay:
    environment:
      - RELAY_HEALTH_CHECK_TIMEOUT_SECONDS=120  # Wait up to 2 minutes
      - RELAY_HEALTH_CHECK_INTERVAL_SECONDS=5   # Check every 5 seconds
```

#### Per-Container Health Check Timeout

Override the timeout for specific containers:

```yaml
services:
  slow-startup-app:
    image: myapp:1.0.0
    labels:
      - relay.enable=true
      - relay.healthcheck.timeout=180  # 3 minutes for this container
```

### Rolling Update Example

```
[2026-01-11 10:00:00 INF] Starting rolling update for container api: myapp:1.25.0 -> myapp:1.25.1
[2026-01-11 10:00:00 INF] Creating staging container api-relay-staging for health verification...
[2026-01-11 10:00:05 INF] Waiting for staging container to become healthy (timeout: 60s)...
[2026-01-11 10:00:10 INF] Staging container is healthy. Proceeding with switchover...
[2026-01-11 10:00:10 INF] Stopping old container api...
[2026-01-11 10:00:12 INF] Creating final container api with full configuration...
[2026-01-11 10:00:13 INF] Rolling update completed successfully for api (new ID: abc123def456)
```

## Container Labels

Configure update behavior per container using labels:

| Label | Values | Default | Description |
|-------|--------|---------|-------------|
| `relay.enable` | `true` / `false` | - | **Required**. Enable/disable monitoring for this container |
| `relay.update` | `digest` / `patch` / `minor` / `major` | `digest` | Update strategy to use for this container |
| `relay.healthcheck.timeout` | Number (seconds) | (from config) | Override health check timeout for this container |

### Label Examples

```yaml
services:
  # Basic monitoring with default digest strategy
  web:
    image: nginx:1.25
    labels:
      - relay.enable=true

  # Patch updates with custom health check timeout
  api:
    image: myapp:1.25.0
    labels:
      - relay.enable=true
      - relay.update=patch
      - relay.healthcheck.timeout=120

  # Minor updates
  worker:
    image: myapp:1.25.0
    labels:
      - relay.enable=true
      - relay.update=minor

  # Container NOT monitored (no label or relay.enable=false)
  ignored:
    image: myapp:latest
    # No relay.enable label - will be ignored
```

## Private Registry Support

Relay automatically reads Docker credentials from `~/.docker/config.json` to authenticate with private registries.

### Supported Registries

| Registry | Auth Method | Notes |
|----------|-------------|-------|
| Docker Hub | Bearer token | Works with private repositories |
| GitHub Container Registry (ghcr.io) | Bearer token | Requires Personal Access Token (PAT) |
| GitLab Container Registry | Bearer token | Works with GitLab CI/CD tokens |
| Google Container Registry (gcr.io) | Bearer token | Requires service account JSON |
| Azure Container Registry | Bearer token | Works with Azure AD authentication |
| AWS ECR | Basic auth | Requires AWS credentials |
| Generic private registries | Basic auth or Bearer token | Most registries supported |

### Setup Instructions

1. **Log in to your registry** on the host machine:

```bash
# Docker Hub
docker login

# GitHub Container Registry
echo $GITHUB_TOKEN | docker login ghcr.io -u USERNAME --password-stdin

# GitLab Container Registry
docker login registry.gitlab.com -u USERNAME -p $GITLAB_TOKEN

# Private registry
docker login registry.example.com -u USERNAME -p PASSWORD
```

2. **Mount the Docker config** in Relay's docker-compose.yml:

```yaml
services:
  relay:
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - ~/.docker/config.json:/root/.docker/config.json:ro
```

3. **Use private images** with the relay label:

```yaml
services:
  myapp:
    image: ghcr.io/myorg/myapp:1.0.0
    labels:
      - relay.enable=true
      - relay.update=minor
```

### Multiple Registry Support

Relay automatically uses credentials from `config.json` for all configured registries:

```json
{
  "auths": {
    "ghcr.io": {
      "auth": "base64-encoded-token"
    },
    "registry.gitlab.com": {
      "auth": "base64-encoded-token"
    },
    "registry.example.com": {
      "auth": "base64-encoded-token"
    }
  }
}
```

## Usage Examples

### Example 1: Web Application with Patch Updates

```yaml
services:
  relay:
    image: relay:latest
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
    environment:
      - RELAY_CHECK_INTERVAL_SECONDS=300

  webapp:
    image: nginx:1.25.0
    ports:
      - "80:80"
    labels:
      - relay.enable=true
      - relay.update=patch  # Only update to 1.25.x versions
```

### Example 2: API Service with Minor Updates

```yaml
services:
  api:
    image: myorg/api:1.5.0
    ports:
      - "8080:8080"
    environment:
      - DATABASE_URL=postgres://...
    volumes:
      - ./config:/app/config
    labels:
      - relay.enable=true
      - relay.update=minor  # Update to any 1.x.x version
      - relay.healthcheck.timeout=90  # Longer timeout for slow startup
```

### Example 3: Development Environment with Major Updates

```yaml
services:
  dev-app:
    image: myorg/app:latest
    labels:
      - relay.enable=true
      - relay.update=major  # Accept any newer version
```

### Example 4: Production with Digest Strategy

```yaml
services:
  production-app:
    image: myorg/app:v1.2.3
    labels:
      - relay.enable=true
      # Uses default 'digest' strategy - only updates if v1.2.3 is rebuilt
```

### Example 5: Multiple Services with Different Strategies

```yaml
services:
  # Database - no updates (security)
  postgres:
    image: postgres:15
    # No relay.enable label - not monitored

  # Web server - patch updates only
  nginx:
    image: nginx:1.25.0
    labels:
      - relay.enable=true
      - relay.update=patch

  # Application - minor updates
  app:
    image: myorg/app:2.5.0
    labels:
      - relay.enable=true
      - relay.update=minor

  # Monitoring - major updates
  prometheus:
    image: prom/prometheus:latest
    labels:
      - relay.enable=true
      - relay.update=major
```

## How It Works

### Update Detection Flow

1. **Discovery**: Relay periodically queries Docker for running containers with the `relay.enable=true` label
2. **Strategy Check**: Reads the `relay.update` label to determine the update strategy
3. **Update Detection**:
   - **Digest strategy**: Pulls the same tag and compares image digests
   - **Version strategies**: Queries the registry for available tags, finds newer versions matching the strategy
4. **Update Process** (Rolling Update):
   - Creates staging container without port bindings
   - Waits for health check to pass
   - Stops and removes old container
   - Creates final container with full configuration
   - Starts the new container
5. **Cleanup**: Optionally removes old images if `RELAY_CLEANUP_OLD_IMAGES=true`

### Update Process (Legacy Mode)

When rolling updates are disabled:

1. Stops the running container
2. Removes the old container
3. Creates a new container with the same configuration but the new image
4. Starts the new container

**Note**: Legacy mode causes brief downtime during updates.

## Logging

Relay provides structured logs for monitoring and debugging:

### Log Levels

- **INFO**: Normal operations, update cycles, successful updates
- **WARNING**: Non-critical issues, failed health checks, skipped updates
- **ERROR**: Critical errors, failed updates, Docker API errors
- **DEBUG**: Detailed diagnostic information (enable with log level configuration)

### Example Log Output

```
[2026-01-11 10:00:00 INF] Starting Relay - Docker Container Auto-Updater
[2026-01-11 10:00:00 INF] Relay started. Monitoring containers with label: relay.enable=true. Check interval: 300 seconds
[2026-01-11 10:00:00 INF] Running initial check...
[2026-01-11 10:00:00 INF] Check cycle started. Found 3 monitored container(s)
[2026-01-11 10:00:01 INF] Container webapp (nginx:1.25) - No update available
[2026-01-11 10:00:02 INF] Newer version found for container api: 1.25.0 -> 1.25.1
[2026-01-11 10:00:02 INF] Container api - Update detected: myapp:1.25.0 -> myapp:1.25.1
[2026-01-11 10:00:02 INF] Starting rolling update for container api: myapp:1.25.0 -> myapp:1.25.1
[2026-01-11 10:00:02 INF] Creating staging container api-relay-staging for health verification...
[2026-01-11 10:00:07 INF] Waiting for staging container to become healthy (timeout: 60s)...
[2026-01-11 10:00:12 INF] Staging container is healthy. Proceeding with switchover...
[2026-01-11 10:00:12 INF] Stopping old container api...
[2026-01-11 10:00:13 INF] Creating final container api with full configuration...
[2026-01-11 10:00:14 INF] Rolling update completed successfully for api (new ID: abc123def456)
[2026-01-11 10:00:14 INF] Check cycle completed. Checked: 3, Updated: 1, Failed: 0, Unchanged: 2
```

### Viewing Logs

```bash
# Docker Compose
docker compose logs -f relay

# Docker Run
docker logs -f relay

# Last 100 lines
docker logs --tail 100 relay
```

## Building from Source

### Prerequisites

- .NET 10 SDK
- Docker (for building the container image)

### Build

```bash
# Build the application
cd src/Relay
dotnet build

# Build in Release mode
dotnet build -c Release
```

### Run Locally

```bash
# Run from source
cd src/Relay
dotnet run

# Or run with custom configuration
RELAY_CHECK_INTERVAL_SECONDS=60 dotnet run
```

### Build Docker Image

```bash
# Build from project root
docker build -t relay:latest -f docker/Dockerfile .

# Or using docker-compose
cd docker
docker compose build
```

## Testing

Relay includes comprehensive unit tests. Run tests with:

```bash
# Run all tests
cd tests/Relay.Tests
dotnet test

# Run with verbose output
dotnet test --verbosity normal

# Run specific test class
dotnet test --filter "FullyQualifiedName~VersionServiceTests"
```

### Test Coverage

- **150+ unit tests** covering:
  - Version parsing and comparison
  - Update strategy logic
  - Rolling update flow
  - Container monitoring
  - Image checking
  - Error handling

### CI/CD

Relay includes a GitHub Actions workflow (`.github/workflows/ci.yml`) that:
- Builds the solution on every push
- Runs all unit tests
- Uploads test results as artifacts

## Troubleshooting

### Container Not Updating

**Problem**: Container has `relay.enable=true` but isn't updating.

**Solutions**:
1. Check logs: `docker logs relay`
2. Verify label is set: `docker inspect <container> | grep relay`
3. Check if update is available (digest strategy requires image rebuild)
4. Verify registry access (for version strategies)

### Health Check Timeout

**Problem**: Rolling updates fail with health check timeout.

**Solutions**:
1. Increase timeout: `RELAY_HEALTH_CHECK_TIMEOUT_SECONDS=120`
2. Set per-container timeout: `relay.healthcheck.timeout=180`
3. Add Docker HEALTHCHECK to your image
4. Check if container is actually starting correctly

### Registry Authentication Errors

**Problem**: Cannot pull images from private registry.

**Solutions**:
1. Verify Docker login: `docker login <registry>`
2. Check config.json is mounted: `docker exec relay cat /root/.docker/config.json`
3. Verify credentials are valid: `docker pull <image>` on host
4. Check network connectivity to registry

### Port Binding Conflicts

**Problem**: Error about port already in use during update.

**Solutions**:
1. Ensure rolling updates are enabled (default)
2. Staging containers don't use ports, so this shouldn't happen
3. If using legacy mode, ensure old container is fully stopped

### Update Strategy Not Working

**Problem**: Version-based strategies not finding updates.

**Solutions**:
1. Verify tag format is semver-compatible: `1.25.0` not `v1.25.0` (prefixes are supported)
2. Check registry API is accessible
3. Verify tags exist in registry: `docker pull <image>:<tag>`
4. Check logs for registry query errors

### Container Configuration Lost

**Problem**: Updated container missing volumes, networks, or environment variables.

**Solutions**:
1. This shouldn't happen - Relay preserves all configuration
2. Check logs for errors during container inspection
3. Verify original container configuration: `docker inspect <old-container>`
4. Report as bug if configuration is actually lost

## Architecture

```
Relay/
├── src/Relay/
│   ├── Configuration/
│   │   └── RelayOptions.cs             # Application settings
│   ├── Models/
│   │   ├── MonitoredContainer.cs       # Container state model
│   │   ├── ImageUpdateResult.cs        # Update check result
│   │   ├── UpdateStrategy.cs           # Update strategy enum & extensions
│   │   └── DockerCredentials.cs        # Registry credentials model
│   ├── Services/
│   │   ├── DockerService.cs            # Docker API wrapper
│   │   ├── DockerConfigService.cs      # Docker config.json reader
│   │   ├── RegistryService.cs          # Docker registry API client
│   │   ├── VersionService.cs           # Semantic version parsing
│   │   ├── ImageCheckerService.cs      # Image update detection
│   │   ├── ContainerUpdaterService.cs  # Container replacement (rolling updates)
│   │   └── ContainerMonitorService.cs  # Orchestration
│   ├── Workers/
│   │   └── RelayWorker.cs              # Background service
│   └── Program.cs                      # Entry point & DI
├── tests/Relay.Tests/
│   ├── Services/                       # Service unit tests
│   └── Models/                         # Model unit tests
└── docker/
    ├── Dockerfile
    ├── docker-compose.yml
    └── docker-compose.test.yml
```

### Service Responsibilities

- **RelayWorker**: Background service that runs check cycles at intervals
- **ContainerMonitorService**: Orchestrates the update process for all containers
- **ImageCheckerService**: Detects available updates based on strategy
- **ContainerUpdaterService**: Performs rolling updates or legacy updates
- **DockerService**: Wraps Docker API operations
- **RegistryService**: Queries Docker registries for available tags
- **VersionService**: Parses and compares semantic versions

## License

MIT License
