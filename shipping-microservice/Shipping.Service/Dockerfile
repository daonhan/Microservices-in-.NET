FROM mcr.microsoft.com/dotnet/aspnet:10.0 as base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
COPY ["shipping-microservice/Shipping.Service/Shipping.Service.csproj", "Shipping.Service/"]
COPY ["local-nuget-packages/", "/local-nuget-packages/"]
COPY ["shipping-microservice/nuget.config", "./"]
RUN dotnet restore "Shipping.Service/Shipping.Service.csproj"

COPY ["shipping-microservice/Shipping.Service/", "Shipping.Service/"]
WORKDIR "Shipping.Service"
RUN dotnet build "Shipping.Service.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build as publish
RUN dotnet publish "Shipping.Service.csproj" -c $BUILD_CONFIGURATION -o /app/publish

FROM base as final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Shipping.Service.dll"]
