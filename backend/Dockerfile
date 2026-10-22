# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached while only source files change.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/SportsBooking.Domain/SportsBooking.Domain.csproj src/SportsBooking.Domain/
COPY src/SportsBooking.Application/SportsBooking.Application.csproj src/SportsBooking.Application/
COPY src/SportsBooking.Infrastructure/SportsBooking.Infrastructure.csproj src/SportsBooking.Infrastructure/
COPY src/SportsBooking.Api/SportsBooking.Api.csproj src/SportsBooking.Api/
RUN dotnet restore src/SportsBooking.Api/SportsBooking.Api.csproj

COPY src/ src/
RUN dotnet publish src/SportsBooking.Api/SportsBooking.Api.csproj \
    -c Release -o /app --no-restore \
    -p:OpenApiGenerateDocumentsOnBuild=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "SportsBooking.Api.dll"]
