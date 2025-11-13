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
RUN dotnet restore -r linux-musl-x64

# Copy everything else
COPY rancher-devops-operator/. ./rancher-devops-operator/

# Build and publish with AOT - with optimizations for smaller size
WORKDIR /source/rancher-devops-operator
RUN dotnet publish -c Release -r linux-musl-x64 --no-restore -o /app --self-contained \
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
