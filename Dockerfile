# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first (layer cache optimization)
COPY Velox.sln* .
COPY Velox.Shared/Velox.Shared.csproj Velox.Shared/
COPY VeloxAPI/VeloxAPI.csproj VeloxAPI/
COPY VeloxAPI.Tests/VeloxAPI.Tests.csproj VeloxAPI.Tests/

RUN dotnet restore VeloxAPI/VeloxAPI.csproj

# Copy source
COPY Velox.Shared/ Velox.Shared/
COPY VeloxAPI/ VeloxAPI/

RUN dotnet publish VeloxAPI/VeloxAPI.csproj -c Release -o /app/publish --no-restore

# ── Stage 2: Runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# SQLite data directory — Railway mounts a persistent volume here
ENV VELOX_DB_PATH=/data
RUN mkdir -p /data

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "VeloxAPI.dll"]
