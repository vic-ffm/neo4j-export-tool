.PHONY: help clean dist-clean binaries macos-apple-silicon macos-intel linux-amd64 linux-arm64 windows-x64 windows-arm64 docker-image

VERSION := $(shell cat .version)

help:
	@echo "Neo4j Export Tool v$(VERSION) - Build Targets"
	@echo ""
	@echo "Binary builds (using Docker):"
	@echo "  make macos-apple-silicon - Build macOS Apple Silicon (M1/M2/M3) binary"
	@echo "  make macos-intel         - Build macOS Intel binary"
	@echo "  make linux-amd64         - Build Linux AMD64 binary"
	@echo "  make linux-arm64         - Build Linux ARM64 binary"
	@echo "  make windows-x64         - Build Windows x64 binary"
	@echo "  make windows-arm64       - Build Windows ARM64 binary"
	@echo "  make binaries            - Build all platform binaries"
	@echo ""
	@echo "Docker operations:"
	@echo "  make docker-image   - Build the Docker image for running"
	@echo "  make docker-run     - Run the export using Docker"
	@echo ""
	@echo "Cleanup:"
	@echo "  make clean          - Clean build artifacts"
	@echo "  make dist-clean     - Clean distribution directory"

dist:
	@mkdir -p dist

dist-clean:
	@echo "Cleaning distribution directory..."
	@rm -rf dist/*

clean: dist-clean
	@echo "Cleaning build artifacts..."
	@docker image prune -f

docker-image:
	@echo "Building Docker image version $(VERSION)..."
	@VERSION=$(VERSION) docker compose -f neo4j-export-runner.compose.yaml build

docker-run:
	@echo "Running Neo4j export..."
	@VERSION=$(VERSION) docker compose -f neo4j-export-runner.compose.yaml run --rm neo4j-export

macos-apple-silicon: dist
	@echo "Building macOS Apple Silicon (M1/M2/M3) binary..."
	@docker build -f Dockerfile.binaries --target macos-apple-silicon-export --output type=local,dest=./dist .
	@chmod +x dist/neo4j-export-darwin-arm64
	@echo "✓ Binary created: dist/neo4j-export-darwin-arm64"
	@ls -lh dist/neo4j-export-darwin-arm64

macos-intel: dist
	@echo "Building macOS Intel binary..."
	@docker build -f Dockerfile.binaries --target macos-intel-export --output type=local,dest=./dist .
	@chmod +x dist/neo4j-export-darwin-amd64
	@echo "✓ Binary created: dist/neo4j-export-darwin-amd64"
	@ls -lh dist/neo4j-export-darwin-amd64

linux-amd64: dist
	@echo "Building Linux AMD64 binary..."
	@docker build -f Dockerfile.binaries --target linux-amd64-export --output type=local,dest=./dist .
	@chmod +x dist/neo4j-export-linux-amd64
	@echo "✓ Binary created: dist/neo4j-export-linux-amd64"
	@ls -lh dist/neo4j-export-linux-amd64

linux-arm64: dist
	@echo "Building Linux ARM64 binary..."
	@docker build -f Dockerfile.binaries --target linux-arm64-export --output type=local,dest=./dist .
	@chmod +x dist/neo4j-export-linux-arm64
	@echo "✓ Binary created: dist/neo4j-export-linux-arm64"
	@ls -lh dist/neo4j-export-linux-arm64

windows-x64: dist
	@echo "Building Windows x64 binary..."
	@docker build -f Dockerfile.binaries --target windows-x64-export --output type=local,dest=./dist .
	@echo "✓ Binary created: dist/neo4j-export-windows-amd64.exe"
	@ls -lh dist/neo4j-export-windows-amd64.exe

windows-arm64: dist
	@echo "Building Windows ARM64 binary..."
	@docker build -f Dockerfile.binaries --target windows-arm64-export --output type=local,dest=./dist .
	@echo "✓ Binary created: dist/neo4j-export-windows-arm64.exe"
	@ls -lh dist/neo4j-export-windows-arm64.exe

binaries: dist
	@echo "Building all platform binaries..."
	@docker build -f Dockerfile.binaries --target all-binaries --output type=local,dest=./dist .
	@chmod +x dist/neo4j-export-darwin-arm64
	@chmod +x dist/neo4j-export-darwin-amd64
	@chmod +x dist/neo4j-export-linux-amd64
	@chmod +x dist/neo4j-export-linux-arm64
	@echo ""
	@echo "✓ All binaries created:"
	@ls -lh dist/