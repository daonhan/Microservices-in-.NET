FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
COPY ["Directory.Build.props", "./"]
COPY [".editorconfig", "./"]
COPY ["payment-microservice/Payment.Service/Payment.Service.csproj", "Payment.Service/"]
COPY ["local-nuget-packages/", "/local-nuget-packages/"]
COPY ["payment-microservice/nuget.config", "./"]
RUN dotnet restore "Payment.Service/Payment.Service.csproj"

COPY ["payment-microservice/Payment.Service/", "Payment.Service/"]
WORKDIR /Payment.Service
RUN dotnet build "Payment.Service.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
RUN dotnet publish "Payment.Service.csproj" -c $BUILD_CONFIGURATION -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Payment.Service.dll"]
