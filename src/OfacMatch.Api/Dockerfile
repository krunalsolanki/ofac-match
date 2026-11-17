# OfacMatch REST API, self-hosted: the names you screen never leave your server.
# Build from the repository root:
#   docker build -f src/OfacMatch.Api/Dockerfile -t ofacmatch-api .
# Run (downloads the SDN list from OFAC at start-up and again every 24 hours):
#   docker run -p 8080:8080 ofacmatch-api
# Offline, with your own copy of SDN.CSV, ALT.CSV and ADD.CSV:
#   docker run -p 8080:8080 -v /path/to/sdn:/data:ro -e OFACMATCH_DATA_DIR=/data ofacmatch-api
# Require a key on every /v1 request:
#   docker run -p 8080:8080 -e OFACMATCH_API_KEY=change-me ofacmatch-api

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/OfacMatch.Api --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    OFACMATCH_CACHE_DIR=/home/app/ofacmatch
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "OfacMatch.Api.dll"]
