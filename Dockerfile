FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for efficient Docker layer caching
COPY ["Velox.Shared/Velox.Shared.csproj", "Velox.Shared/"]
COPY ["VeloxAPI/VeloxAPI.csproj", "VeloxAPI/"]
RUN dotnet restore "VeloxAPI/VeloxAPI.csproj"

# Copy remaining source code and publish
COPY . .
WORKDIR "/src/VeloxAPI"
RUN dotnet publish "VeloxAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080

RUN mkdir -p /data /app/releases

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV VELOX_DB_PATH=/data

ENTRYPOINT ["dotnet", "VeloxAPI.dll"]
