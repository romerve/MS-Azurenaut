FROM mcr.microsoft.com/dotnet/sdk:6.0-jammy AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/Legacy.Ordering.Api/Legacy.Ordering.Api.csproj src/Legacy.Ordering.Api/
RUN dotnet restore src/Legacy.Ordering.Api/Legacy.Ordering.Api.csproj
COPY src/Legacy.Ordering.Api/ src/Legacy.Ordering.Api/
RUN dotnet publish src/Legacy.Ordering.Api/Legacy.Ordering.Api.csproj \
    --configuration Release --output /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:6.0-jammy AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
RUN addgroup --system app \
    && adduser --system --ingroup app app \
    && mkdir -p /app/data \
    && chown -R app:app /app
COPY --from=build --chown=app:app /app/publish .
USER app
ENTRYPOINT ["dotnet", "Legacy.Ordering.Api.dll"]
