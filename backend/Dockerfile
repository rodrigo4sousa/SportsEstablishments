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
# The image's default would bind port 8080 next to the address below.
ENV ASPNETCORE_HTTP_PORTS=
USER $APP_UID
EXPOSE 8080
# Listens on 0.0.0.0 and on the port the host gives in PORT (Render sets it), 8080 otherwise. `exec` keeps the API as PID 1, so it gets
# the stop signal; the arguments after the image name reach the API (seed-demo, set-admin-password).
ENTRYPOINT ["/bin/sh", "-c", "export ASPNETCORE_URLS=\"http://0.0.0.0:${PORT:-8080}\"; exec dotnet SportsBooking.Api.dll \"$@\"", "--"]
