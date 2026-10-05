# syntax=docker/dockerfile:1
# Build context: repository root.  docker build -f docker/worker.Dockerfile -t bookingplatform/worker .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json ./
COPY Backend/Directory.Build.props Backend/Directory.Packages.props Backend/
COPY Backend/BookingPlatform.Domain/BookingPlatform.Domain.csproj Backend/BookingPlatform.Domain/
COPY Backend/BookingPlatform.Application/BookingPlatform.Application.csproj Backend/BookingPlatform.Application/
COPY Backend/BookingPlatform.Infrastructure/BookingPlatform.Infrastructure.csproj Backend/BookingPlatform.Infrastructure/
COPY Backend/BookingPlatform.Worker/BookingPlatform.Worker.csproj Backend/BookingPlatform.Worker/
RUN dotnet restore Backend/BookingPlatform.Worker/BookingPlatform.Worker.csproj

COPY Backend/ Backend/
RUN dotnet publish Backend/BookingPlatform.Worker/BookingPlatform.Worker.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "BookingPlatform.Worker.dll"]
