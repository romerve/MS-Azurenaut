FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["src/Checkout.Api/Checkout.Api.csproj", "src/Checkout.Api/"]
RUN dotnet restore "src/Checkout.Api/Checkout.Api.csproj"
COPY ["src/Checkout.Api/", "src/Checkout.Api/"]
RUN dotnet publish "src/Checkout.Api/Checkout.Api.csproj" -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Checkout.Api.dll"]
