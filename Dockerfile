# Multi-stage Dockerfile for IVA Scanner
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000
EXPOSE 7000

# Lightweight base: only curl is needed (SQLite is bundled with EF Core, no native DB server required)
RUN apt-get update && \
    apt-get install -y curl && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY ["Master/IvaScanner.Master.csproj", "Master/"]
COPY ["IvaScanner.Core/IvaScanner.Core.csproj", "IvaScanner.Core/"]
COPY ["Worker/IvaScanner.Worker.csproj", "Worker/"]
COPY ["IvaScanner.sln", "./"]

# Restore packages
RUN dotnet restore "IvaScanner.sln"

# Copy source code
COPY . .

# Build Master
WORKDIR "/src/Master"
RUN dotnet build "IvaScanner.Master.csproj" -c Release -o /app/build/master

# Build Worker  
WORKDIR "/src/Worker"
RUN dotnet build "IvaScanner.Worker.csproj" -c Release -o /app/build/worker

FROM build AS publish-master
WORKDIR "/src/Master"
RUN dotnet publish "IvaScanner.Master.csproj" -c Release -o /app/publish/master

FROM build AS publish-worker
WORKDIR "/src/Worker"
RUN dotnet publish "IvaScanner.Worker.csproj" -c Release -o /app/publish/worker

# Master runtime image
FROM base AS master
WORKDIR /app
COPY --from=publish-master /app/publish/master .
ENTRYPOINT ["dotnet", "IvaScanner.Master.dll"]

# Worker runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS worker
WORKDIR /app
COPY --from=publish-worker /app/publish/worker .

# Create working directory for worker
RUN mkdir -p /app/temp && \
    mkdir -p /app/logs && \
    chown -R app:app /app

USER app
ENTRYPOINT ["dotnet", "IvaScanner.Worker.dll"]