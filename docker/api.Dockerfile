# syntax=docker/dockerfile:1
# Build context: repository root.  docker build -f docker/api.Dockerfile -t bookingplatform/api .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json ./
COPY Backend/Directory.Build.props Backend/Directory.Packages.props Backend/
COPY Backend/BookingPlatform.Domain/BookingPlatform.Domain.csproj Backend/BookingPlatform.Domain/
COPY Backend/BookingPlatform.Application/BookingPlatform.Application.csproj Backend/BookingPlatform.Application/
COPY Backend/BookingPlatform.Infrastructure/BookingPlatform.Infrastructure.csproj Backend/BookingPlatform.Infrastructure/
COPY Backend/BookingPlatform.Migrations/BookingPlatform.Migrations.csproj Backend/BookingPlatform.Migrations/
COPY Backend/BookingPlatform.Api/BookingPlatform.Api.csproj Backend/BookingPlatform.Api/
RUN dotnet restore Backend/BookingPlatform.Api/BookingPlatform.Api.csproj

COPY Backend/ Backend/
# The Api references Migrations, which embeds the SQL scripts.
COPY database/migrations/ database/migrations/
RUN dotnet publish Backend/BookingPlatform.Api/BookingPlatform.Api.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "BookingPlatform.Api.dll"]
