#!/bin/bash

# Relay Docker Hub Publishing Script
# Usage: ./publish.sh [dockerhub-username] [version]
#   If version is not provided, uses the latest git tag
# Example: ./publish.sh myusername
# Example: ./publish.sh myusername 1.0.0

set -e

DOCKERHUB_USERNAME=${1:-}
VERSION=${2:-}

# Supported platforms (ARM v7 not supported by .NET 10)
PLATFORMS="linux/amd64,linux/arm64"

if [ -z "$DOCKERHUB_USERNAME" ]; then
    echo "Error: Docker Hub username required"
    echo "Usage: $0 [dockerhub-username] [version]"
    echo "Example: $0 myusername"
    echo "Example: $0 myusername 1.0.0"
    echo ""
    echo "If version is not provided, the latest git tag will be used."
    exit 1
fi

# Get version from git tag if not provided
if [ -z "$VERSION" ]; then
    # Get the latest tag
    LATEST_TAG=$(git describe --tags --abbrev=0 2>/dev/null || echo "")
    
    if [ -z "$LATEST_TAG" ]; then
        echo "Error: No git tags found and no version provided"
        echo "Please either:"
        echo "  1. Create a git tag: git tag v1.0.0"
        echo "  2. Or provide version: $0 $DOCKERHUB_USERNAME 1.0.0"
        exit 1
    fi
    
    # Remove 'v' prefix if present (e.g., v1.0.0 -> 1.0.0)
    VERSION=${LATEST_TAG#v}
    echo "Using version from git tag: $LATEST_TAG -> $VERSION"
else
    # Remove 'v' prefix if user provided it
    VERSION=${VERSION#v}
fi

echo "=========================================="
echo "Publishing Relay to Docker Hub"
echo "=========================================="
echo "Docker Hub username: $DOCKERHUB_USERNAME"
echo "Version: $VERSION"
echo "Platforms: $PLATFORMS"
echo ""

# Extract major and minor versions
MAJOR=$(echo $VERSION | cut -d. -f1)
MINOR=$(echo $VERSION | cut -d. -f1-2)

echo "Tags to create:"
echo "  - $DOCKERHUB_USERNAME/relay:$VERSION"
echo "  - $DOCKERHUB_USERNAME/relay:$MINOR"
echo "  - $DOCKERHUB_USERNAME/relay:$MAJOR"
echo "  - $DOCKERHUB_USERNAME/relay:latest"
echo ""

# Check if buildx is available
if ! docker buildx version > /dev/null 2>&1; then
    echo "Error: Docker Buildx is required for multi-architecture builds"
    echo "Please install Docker Buildx or update Docker Desktop"
    exit 1
fi

# Create and use a buildx builder instance if it doesn't exist
BUILDER_NAME="relay-builder"
if ! docker buildx inspect $BUILDER_NAME > /dev/null 2>&1; then
    echo "Creating buildx builder: $BUILDER_NAME"
    docker buildx create --name $BUILDER_NAME --use
    docker buildx inspect --bootstrap
else
    echo "Using existing buildx builder: $BUILDER_NAME"
    docker buildx use $BUILDER_NAME
fi

# Login to Docker Hub
echo ""
echo "Logging in to Docker Hub..."
docker login

# Build and push multi-architecture images
echo ""
echo "Building and pushing multi-architecture images..."
echo "This may take several minutes..."

docker buildx build \
    --platform $PLATFORMS \
    --file docker/Dockerfile \
    --tag $DOCKERHUB_USERNAME/relay:$VERSION \
    --tag $DOCKERHUB_USERNAME/relay:$MINOR \
    --tag $DOCKERHUB_USERNAME/relay:$MAJOR \
    --tag $DOCKERHUB_USERNAME/relay:latest \
    --push \
    .

echo ""
echo "✅ Successfully published Relay to Docker Hub!"
echo ""
echo "Published tags (multi-architecture):"
echo "  - $DOCKERHUB_USERNAME/relay:$VERSION"
echo "  - $DOCKERHUB_USERNAME/relay:$MINOR"
echo "  - $DOCKERHUB_USERNAME/relay:$MAJOR"
echo "  - $DOCKERHUB_USERNAME/relay:latest"
echo ""
echo "Platforms: $PLATFORMS"
echo "View at: https://hub.docker.com/r/$DOCKERHUB_USERNAME/relay"
