FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
COPY ["Directory.Build.props", "./"]
COPY [".editorconfig", "./"]
COPY ["api-gateway/ApiGateway/ApiGateway.csproj", "ApiGateway/"]
COPY ["local-nuget-packages/", "/local-nuget-packages/"]
COPY ["api-gateway/nuget.config", "./"]
RUN dotnet restore "ApiGateway/ApiGateway.csproj"

COPY ["api-gateway/ApiGateway/", "ApiGateway/"]
WORKDIR /ApiGateway
RUN dotnet build "ApiGateway.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
RUN dotnet publish "ApiGateway.csproj" -c $BUILD_CONFIGURATION -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ApiGateway.dll"]
