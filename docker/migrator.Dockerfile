# syntax=docker/dockerfile:1
# Build context: repository root.  docker build -f docker/migrator.Dockerfile -t bookingplatform/migrator .
# Run: docker run --rm -e ConnectionStrings__Postgres=... bookingplatform/migrator

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json ./
COPY Backend/Directory.Build.props Backend/Directory.Packages.props Backend/
COPY Backend/BookingPlatform.Migrations/BookingPlatform.Migrations.csproj Backend/BookingPlatform.Migrations/
RUN dotnet restore Backend/BookingPlatform.Migrations/BookingPlatform.Migrations.csproj

COPY Backend/BookingPlatform.Migrations/ Backend/BookingPlatform.Migrations/
COPY database/migrations/ database/migrations/
RUN dotnet publish Backend/BookingPlatform.Migrations/BookingPlatform.Migrations.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "BookingPlatform.Migrations.dll"]
CMD ["migrate"]
