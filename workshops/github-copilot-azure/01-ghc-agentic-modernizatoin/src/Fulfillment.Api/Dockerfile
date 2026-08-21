FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/Fulfillment.Api/Fulfillment.Api.csproj src/Fulfillment.Api/
RUN dotnet restore src/Fulfillment.Api/Fulfillment.Api.csproj
COPY src/Fulfillment.Api/ src/Fulfillment.Api/
RUN dotnet publish src/Fulfillment.Api/Fulfillment.Api.csproj \
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
ENTRYPOINT ["dotnet", "Fulfillment.Api.dll"]
