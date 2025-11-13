# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
ARG TARGETARCH
WORKDIR /source

# Install native AOT prerequisites
RUN apk add --no-cache clang build-base zlib-dev

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
    zlib

# Create non-root user
RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

# Copy the published application
COPY --from=build /app/rancher-devops-operator .

ENTRYPOINT ["./rancher-devops-operator"]
