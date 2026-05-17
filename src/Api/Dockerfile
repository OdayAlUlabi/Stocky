# Multi-stage build for Stocky.Api (.NET 10) + ACA migrator job.
# Build context = repo root so we can copy the .csproj + sources.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Api/Stocky.Api.csproj src/Api/
RUN dotnet restore src/Api/Stocky.Api.csproj
COPY src/Api/ src/Api/
RUN dotnet publish src/Api/Stocky.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
    # Run as non-root (user 'app' already exists in the aspnet base image).
RUN chown -R app:app /app
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=false
EXPOSE 8080
COPY --from=build --chown=app:app /app/publish .
ENTRYPOINT ["dotnet", "Stocky.Api.dll"]
