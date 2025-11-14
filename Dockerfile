# syntax=docker/dockerfile:1
FROM --platform=$TARGETPLATFORM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
ARG TARGETARCH
WORKDIR /source

# Install native AOT prerequisites
RUN apk add --no-cache clang lld build-base musl-dev zlib-dev

# Copy project files
COPY rancher-devops-operator/*.csproj ./rancher-devops-operator/
COPY *.sln ./

# Restore dependencies (framework-dependent now; no RID-specific restore needed)
RUN dotnet restore

# Install KubeOps CLI tool (optional) – invoke using 'kubeops', not 'dotnet kubeops'
ENV PATH="/root/.dotnet/tools:$PATH"
RUN dotnet tool install --global KubeOps.Cli || echo "KubeOps CLI optional; continuing"

# (Generation is handled by KubeOps.Generator package during build; explicit CLI build removed to avoid failure)

# Copy everything else
COPY rancher-devops-operator/. ./rancher-devops-operator/

WORKDIR /source/rancher-devops-operator
# Framework-dependent publish (smaller image than self-contained); AOT disabled
RUN dotnet publish -c Release -o /app --no-restore

# (Optional strip removed – not applicable / less beneficial for framework-dependent build)

# Final stage - using Alpine for minimal size
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final
WORKDIR /app

# Base image already contains required ASP.NET & ICU dependencies

# Create non-root user
RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

# Copy all publish output (binary + dlls etc.)
COPY --from=build /app/ ./

ENTRYPOINT ["dotnet", "rancher-devops-operator.dll"]
