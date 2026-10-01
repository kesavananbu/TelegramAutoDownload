# syntax=docker/dockerfile:1.7

# ── Build stage (always on the builder CPU — avoids QEMU segfaults in dotnet restore) ──
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY BasePlugin/BasePlugins.csproj                              BasePlugin/
COPY Logger/Logger.csproj                                       Logger/
COPY TelegramClient/TelegramClient.csproj                       TelegramClient/
COPY DownloadService/DownloadPlugin.csproj                      DownloadService/
COPY YoutubePlugin/YoutubePlugin.csproj                         YoutubePlugin/
COPY SocialMediaPlugin/SocialMediaPlugin.csproj                 SocialMediaPlugin/
COPY TorrentPlugin/TorrentPlugin.csproj                         TorrentPlugin/
COPY TelegramAutoDownload.Headless/TelegramAutoDownload.Headless.csproj TelegramAutoDownload.Headless/
RUN dotnet restore TelegramAutoDownload.Headless/TelegramAutoDownload.Headless.csproj

COPY BasePlugin/                              BasePlugin/
COPY Logger/                                  Logger/
COPY TelegramClient/                          TelegramClient/
COPY DownloadService/                         DownloadService/
COPY YoutubePlugin/                           YoutubePlugin/
COPY SocialMediaPlugin/                       SocialMediaPlugin/
COPY TorrentPlugin/                           TorrentPlugin/
COPY TelegramAutoDownload.Headless/           TelegramAutoDownload.Headless/

# Cross-compile managed output + RID-specific native assets (e.g. SQLite) per target arch.
RUN case "$TARGETARCH" in \
      amd64) DOTNET_ARCH=x64 ;; \
      arm64) DOTNET_ARCH=arm64 ;; \
      arm)   DOTNET_ARCH=arm ;; \
      *) echo "Unsupported TARGETARCH=$TARGETARCH"; exit 1 ;; \
    esac \
 && dotnet publish TelegramAutoDownload.Headless/TelegramAutoDownload.Headless.csproj \
    -c Release -o /out --no-self-contained -a "$DOTNET_ARCH" /p:UseAppHost=false

# ── Runtime stage ────────────────────────────────────────────────────────────
# Official aspnet images already ship a non-root `app` user (APP_UID=1654).
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
ARG APP_UID=1654
ARG APP_USER=app
ENV DEBIAN_FRONTEND=noninteractive

USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      yt-dlp ffmpeg ca-certificates curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build --chown=${APP_UID}:${APP_UID} /out .

ENV DATA_DIR=/data \
    DOWNLOADS_DIR=/downloads \
    XDG_CONFIG_HOME=/data \
    LISTEN_URL=http://0.0.0.0:8080 \
    ASPNETCORE_URLS=http://0.0.0.0:8080

RUN mkdir -p /data /downloads \
 && chown -R ${APP_UID}:${APP_UID} /data /downloads

VOLUME ["/data", "/downloads"]
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
  CMD ["curl", "-fsS", "http://127.0.0.1:8080/api/web-auth/status"]

USER ${APP_USER}

ENTRYPOINT ["dotnet", "TelegramAutoDownload.Headless.dll"]
