# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/Release.Api/Release.Api.csproj src/Release.Api/
RUN dotnet restore src/Release.Api/Release.Api.csproj
COPY src/Release.Api/ src/Release.Api/
RUN dotnet publish src/Release.Api/Release.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app \
    /p:BuildRevision=container

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "Release.Api.dll"]
