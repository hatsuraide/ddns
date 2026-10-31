FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

ENV DOTNET_EnableDiagnostics=0 \
    DDNS_CONFIG_PATH=/config/ddns.json

RUN mkdir -p /config && chown -R "$APP_UID" /app /config
USER $APP_UID

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Ddns/Ddns.csproj", "Ddns/"]
RUN dotnet restore "Ddns/Ddns.csproj"

COPY ["Ddns/", "Ddns/"]
WORKDIR /src/Ddns
RUN dotnet publish "Ddns.csproj" \
    --configuration "$BUILD_CONFIGURATION" \
    --output /app/publish

FROM runtime AS final
COPY --from=build /app/publish .
VOLUME ["/config"]
ENTRYPOINT ["./ddns"]
