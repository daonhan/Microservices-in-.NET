FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
COPY ["Directory.Build.props", "./"]
COPY [".editorconfig", "./"]
COPY ["saga-microservice/Saga.Service/Saga.Service.csproj", "Saga.Service/"]
COPY ["local-nuget-packages/", "/local-nuget-packages/"]
COPY ["saga-microservice/nuget.config", "./"]
RUN dotnet restore "Saga.Service/Saga.Service.csproj"

COPY ["saga-microservice/Saga.Service/", "Saga.Service/"]
WORKDIR /Saga.Service
RUN dotnet build "Saga.Service.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
RUN dotnet publish "Saga.Service.csproj" -c $BUILD_CONFIGURATION -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Saga.Service.dll"]
