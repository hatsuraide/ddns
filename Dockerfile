FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

ENV DOTNET_EnableDiagnostics=0 \
    DDNS_CONFIG_PATH=/config/ddns.json

RUN mkdir -p /config && chown -R "$APP_UID" /app /config
USER $APP_UID

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
# Set automatically by BuildKit for multi-platform builds (amd64, arm64, arm, 386).
# Empty with the classic builder, where the host architecture is the target.
ARG TARGETARCH
WORKDIR /src

COPY ["Ddns/Ddns.csproj", "Ddns/"]
RUN dotnet restore "Ddns/Ddns.csproj"

COPY ["Ddns/", "Ddns/"]
WORKDIR /src/Ddns
RUN if [ -z "$TARGETARCH" ]; then \
        dotnet publish "Ddns.csproj" \
            --configuration "$BUILD_CONFIGURATION" \
            --output /app/publish; \
    else \
        case "$TARGETARCH" in \
            amd64) dotnet_arch=x64 ;; \
            386) dotnet_arch=x86 ;; \
            *) dotnet_arch="$TARGETARCH" ;; \
        esac; \
        dotnet publish "Ddns.csproj" \
            --configuration "$BUILD_CONFIGURATION" \
            --arch "$dotnet_arch" \
            --output /app/publish; \
    fi

FROM runtime AS final
COPY --from=build /app/publish .
VOLUME ["/config"]
ENTRYPOINT ["./ddns"]
