# Multi-stage build: Build stage using SDK, Runtime stage using Azure Linux Chiseled

# Build stage - Use Azure Linux SDK to compile the F# project
FROM mcr.microsoft.com/dotnet/sdk:9.0-azurelinux3.0 AS build

# Set up build environment
WORKDIR /src

# Copy project file and restore dependencies (better layer caching)
COPY Neo4jExport/Neo4jExport.fsproj ./Neo4jExport/
RUN dotnet restore Neo4jExport/Neo4jExport.fsproj

# Install Fantomas tool for code formatting checks
# Using version 6.3.9 to match project dependency
RUN dotnet tool install -g fantomas --version 6.3.9
ENV PATH="${PATH}:/root/.dotnet/tools"

# Copy source code
COPY Neo4jExport/ ./Neo4jExport/

# Restore again after copying all source to ensure all dependencies are available
RUN dotnet restore Neo4jExport/Neo4jExport.fsproj

# Run formatting check - always show results but don't fail the build
RUN echo "=== Running Fantomas Format Check ===" && \
    fantomas --check Neo4jExport/ || \
    (echo "Code formatting check completed. See differences above." && true)

# Build and publish the project
RUN dotnet publish Neo4jExport/Neo4jExport.fsproj -c Release -o /app/publish

# Runtime stage - Use Azure Linux Chiseled (Distroless) for optimal performance
FROM mcr.microsoft.com/dotnet/runtime:9.0-azurelinux3.0-distroless AS runtime

# Set up the application directory
WORKDIR /app

# Copy the published application from build stage
COPY --from=build /app/publish .

# Chiseled images run as non-root by default - no need for explicit user management

# Define the entry point for the container
ENTRYPOINT ["dotnet", "neo4j-export.dll"]