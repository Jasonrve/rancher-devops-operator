# syntax=docker/dockerfile:1
FROM --platform=$TARGETPLATFORM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
ARG TARGETARCH
WORKDIR /source

# Install native AOT prerequisites
RUN apk add --no-cache clang lld build-base musl-dev zlib-dev

# Copy project files
COPY rancher-devops-operator/*.csproj ./rancher-devops-operator/
COPY *.sln ./

# Restore dependencies for the target architecture with runtime identifier
# Map Docker's TARGETARCH to .NET RID
RUN case "$TARGETARCH" in \
        "amd64") RID=linux-musl-x64 ;; \
        "arm64") RID=linux-musl-arm64 ;; \
        *) echo "Unsupported TARGETARCH: $TARGETARCH" ; exit 1 ;; \
    esac \
    && dotnet restore -r $RID

# Install KubeOps CLI tool and run code generation (needed for proper operator metadata under NativeAOT)
ENV PATH="/root/.dotnet/tools:$PATH"
RUN dotnet tool install --global KubeOps.Cli \
    && dotnet kubeops build --project ./rancher-devops-operator/rancher-devops-operator.csproj

# Copy everything else
COPY rancher-devops-operator/. ./rancher-devops-operator/

# Build and publish with AOT - with optimizations for smaller size
WORKDIR /source/rancher-devops-operator
RUN case "$TARGETARCH" in \
        "amd64") RID=linux-musl-x64 ;; \
        "arm64") RID=linux-musl-arm64 ;; \
        *) echo "Unsupported TARGETARCH: $TARGETARCH" ; exit 1 ;; \
    esac \
    && dotnet publish -c Release -r $RID --no-restore -o /app --self-contained \
        /p:StripSymbols=true \
        /p:EnableCompressionInSingleFile=true

# Strip additional symbols from the binary
RUN strip /app/rancher-devops-operator

# Final stage - using Alpine for minimal size
FROM alpine:3.19 AS final
WORKDIR /app

# Install only the minimal runtime dependencies needed for musl-based AOT binaries
RUN apk add --no-cache \
    libstdc++ \
    libgcc \
    zlib \
    icu-libs

# Create non-root user
RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

# Copy the published application
COPY --from=build /app/rancher-devops-operator .

ENTRYPOINT ["./rancher-devops-operator"]
