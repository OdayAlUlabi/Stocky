# Multi-stage build for Stocky.Web.Mvc (.NET 10 server-rendered MVC).
# Build context = repo root so we can copy both the MVC project and the
# Stocky.Api project (which Web.Mvc references for in-process controller
# delegation via ApiControllerInvoker).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Api/Stocky.Api.csproj src/Api/
COPY src/Web.Mvc/Stocky.Web.Mvc.csproj src/Web.Mvc/
RUN dotnet restore src/Web.Mvc/Stocky.Web.Mvc.csproj
COPY src/Api/ src/Api/
COPY src/Web.Mvc/ src/Web.Mvc/
RUN dotnet publish src/Web.Mvc/Stocky.Web.Mvc.csproj \
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
ENTRYPOINT ["dotnet", "Stocky.Web.Mvc.dll"]
