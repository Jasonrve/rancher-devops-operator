# syntax=docker/dockerfile:1

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
ARG TARGETARCH
ARG BUILDPLATFORM
WORKDIR /operator

RUN echo "TARGETARCH=$TARGETARCH"
RUN echo "BUILDPLATFORM=$BUILDPLATFORM"

# Copy project files first to leverage Docker layer caching
COPY rancher-devops-operator/*.csproj ./
RUN dotnet restore "rancher-devops-operator.csproj"

# Copy the remaining source
COPY rancher-devops-operator/. ./

# Self-contained, single-file, trimmed publish similar to uptime-kuma-operator
RUN dotnet publish "rancher-devops-operator.csproj" -c Release -o out \
	--self-contained true \
	/p:PublishSingleFile=true \
	/p:PublishTrimmed=true \
	/p:EnableCompressionInSingleFile=true \
	/p:TrimMode=partial \
	-a $TARGETARCH

FROM mcr.microsoft.com/dotnet/runtime-deps:9.0-alpine AS final
ARG TARGETARCH
ARG BUILDPLATFORM
WORKDIR /operator
COPY --from=build /operator/out/ ./

RUN chmod -R 755 /operator

ENTRYPOINT ["/operator/rancher-devops-operator"]
