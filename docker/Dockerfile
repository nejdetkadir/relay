# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY src/Relay/Relay.csproj ./Relay/
RUN dotnet restore Relay/Relay.csproj

# Copy source code and build
COPY src/Relay/ ./Relay/
RUN dotnet publish Relay/Relay.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# Copy published application
COPY --from=build /app/publish .

# Set default environment variables
ENV RELAY_CHECK_INTERVAL_SECONDS=300 \
    RELAY_LABEL_KEY=relay.enable \
    RELAY_CLEANUP_OLD_IMAGES=false \
    RELAY_DOCKER_HOST=unix:///var/run/docker.sock \
    RELAY_CHECK_ON_STARTUP=true

ENTRYPOINT ["dotnet", "Relay.dll"]
