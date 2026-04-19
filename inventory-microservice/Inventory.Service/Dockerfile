FROM mcr.microsoft.com/dotnet/aspnet:10.0 as base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
COPY ["inventory-microservice/Inventory.Service/Inventory.Service.csproj", "Inventory.Service/"]
COPY ["local-nuget-packages/", "/local-nuget-packages/"]
COPY ["inventory-microservice/nuget.config", "./"]
RUN dotnet restore "Inventory.Service/Inventory.Service.csproj"

COPY ["inventory-microservice/Inventory.Service/", "Inventory.Service/"]
WORKDIR "Inventory.Service"
RUN dotnet build "Inventory.Service.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build as publish
RUN dotnet publish "Inventory.Service.csproj" -c $BUILD_CONFIGURATION -o /app/publish

FROM base as final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Inventory.Service.dll"]
