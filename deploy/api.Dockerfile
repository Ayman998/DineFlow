# DineFlow API. Build from the repository root:
#   docker build -f deploy/api.Dockerfile -t dineflow-api .
# The projects target .NET 8; any .NET 10 SDK builds them (global.json isn't copied, so its exact pin doesn't apply).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files alone, so code changes don't invalidate the package layer.
COPY DineFlow/DineFlow.Domain/DineFlow.Domain.csproj DineFlow/DineFlow.Domain/
COPY DineFlow/DineFlow.Application/DineFlow.Application.csproj DineFlow/DineFlow.Application/
COPY DineFlow/DineFlow.Infrastructure/DineFlow.Infrastructure.csproj DineFlow/DineFlow.Infrastructure/
COPY DineFlow/DineFlow.Api/DineFlow.Api.csproj DineFlow/DineFlow.Api/
RUN dotnet restore DineFlow/DineFlow.Api/DineFlow.Api.csproj

COPY DineFlow/DineFlow.Domain/ DineFlow/DineFlow.Domain/
COPY DineFlow/DineFlow.Application/ DineFlow/DineFlow.Application/
COPY DineFlow/DineFlow.Infrastructure/ DineFlow/DineFlow.Infrastructure/
COPY DineFlow/DineFlow.Api/ DineFlow/DineFlow.Api/
RUN dotnet publish DineFlow/DineFlow.Api/DineFlow.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

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
ENTRYPOINT ["dotnet", "DineFlow.Api.dll"]
