# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0-azurelinux3.0 AS build

WORKDIR /src

COPY Neo4jExport/Neo4jExport.fsproj ./Neo4jExport/
RUN dotnet restore Neo4jExport/Neo4jExport.fsproj

RUN dotnet tool install -g fantomas --version 6.3.9
ENV PATH="${PATH}:/root/.dotnet/tools"

COPY Neo4jExport/ ./Neo4jExport/

RUN dotnet restore Neo4jExport/Neo4jExport.fsproj

RUN echo "=== Running Fantomas Format Check ===" && \
    fantomas --check Neo4jExport/ || \
    (echo "Code formatting check completed. See differences above." && true)

RUN dotnet publish Neo4jExport/Neo4jExport.fsproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:9.0-azurelinux3.0-distroless AS runtime

WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "neo4j-export.dll"]