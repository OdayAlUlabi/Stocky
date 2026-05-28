# Multi-stage build for Stocky.Mcp (.NET 10).
# Build context = repo root so we can copy the .csproj + sources.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Mcp/Stocky.Mcp.csproj src/Mcp/
RUN dotnet restore src/Mcp/Stocky.Mcp.csproj
COPY src/Mcp/ src/Mcp/
RUN dotnet publish src/Mcp/Stocky.Mcp.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN chown -R app:app /app
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=false
EXPOSE 8080
COPY --from=build --chown=app:app /app/publish .
ENTRYPOINT ["dotnet", "Stocky.Mcp.dll"]
