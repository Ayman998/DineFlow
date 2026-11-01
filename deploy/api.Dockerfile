# helloZen API. Build from the repository root:
#   docker build -f deploy/api.Dockerfile -t hellozen-api .
# The projects target .NET 8; any .NET 10 SDK builds them (global.json isn't copied, so its exact pin doesn't apply).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files alone, so code changes don't invalidate the package layer.
COPY HelloZen/HelloZen.Domain/HelloZen.Domain.csproj HelloZen/HelloZen.Domain/
COPY HelloZen/HelloZen.Application/HelloZen.Application.csproj HelloZen/HelloZen.Application/
COPY HelloZen/HelloZen.Infrastructure/HelloZen.Infrastructure.csproj HelloZen/HelloZen.Infrastructure/
COPY HelloZen/HelloZen.Api/HelloZen.Api.csproj HelloZen/HelloZen.Api/
RUN dotnet restore HelloZen/HelloZen.Api/HelloZen.Api.csproj

COPY HelloZen/HelloZen.Domain/ HelloZen/HelloZen.Domain/
COPY HelloZen/HelloZen.Application/ HelloZen/HelloZen.Application/
COPY HelloZen/HelloZen.Infrastructure/ HelloZen/HelloZen.Infrastructure/
COPY HelloZen/HelloZen.Api/ HelloZen/HelloZen.Api/
RUN dotnet publish HelloZen/HelloZen.Api/HelloZen.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    # Uploaded photos, Data Protection keys and the file outboxes: mount a volume here.
    Storage__LocalRoot=/app/App_Data/media \
    DataProtection__KeysPath=/app/App_Data/keys
COPY --from=build /app .
# The development settings hold development-only keys; production never loads them, so leave them out.
RUN rm -f appsettings.Development.json && mkdir -p App_Data && chown -R $APP_UID App_Data
USER $APP_UID
EXPOSE 8080
# Arguments pass through: `docker compose run --rm api migrate` (see AdminCommands).
ENTRYPOINT ["dotnet", "HelloZen.Api.dll"]
