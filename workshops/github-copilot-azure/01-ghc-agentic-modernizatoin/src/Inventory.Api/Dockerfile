FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/Inventory.Api/Inventory.Api.csproj src/Inventory.Api/
RUN dotnet restore src/Inventory.Api/Inventory.Api.csproj
COPY src/Inventory.Api/ src/Inventory.Api/
RUN dotnet publish src/Inventory.Api/Inventory.Api.csproj \
    --configuration Release --output /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && mkdir -p /app/data \
    && chown -R $APP_UID:$APP_UID /app/data \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Inventory.Api.dll"]
