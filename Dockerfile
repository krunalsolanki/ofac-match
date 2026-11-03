# OfacMatch MCP server (stdio). Used by Glama's checks and other MCP catalogs that run servers in containers.
#   docker build -t ofac-match .
#   docker run -i --rm ofac-match
# The SDN list is downloaded from OFAC on the first tool call. To use an offline copy instead, mount a folder
# with SDN.CSV, ALT.CSV and ADD.CSV and set OFACMATCH_DATA_DIR:
#   docker run -i --rm -v /path/to/sdn:/data -e OFACMATCH_DATA_DIR=/data ofac-match

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/OfacMatch.Mcp --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
# Cache the downloaded list inside the container rather than in a home folder that may not exist.
ENV OFACMATCH_CACHE_DIR=/tmp/ofacmatch
USER app
ENTRYPOINT ["dotnet", "OfacMatch.Mcp.dll"]
