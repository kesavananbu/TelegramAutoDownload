# syntax=docker/dockerfile:1.7

# ── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only csproj files first for better layer caching
COPY BasePlugin/BasePlugins.csproj                              BasePlugin/
COPY Logger/Logger.csproj                                       Logger/
COPY TelegramClient/TelegramClient.csproj                       TelegramClient/
COPY DownloadService/DownloadPlugin.csproj                      DownloadService/
COPY YoutubePlugin/YoutubePlugin.csproj                         YoutubePlugin/
COPY SocialMediaPlugin/SocialMediaPlugin.csproj                 SocialMediaPlugin/
COPY TorrentPlugin/TorrentPlugin.csproj                         TorrentPlugin/
COPY TelegramAutoDownload.Headless/TelegramAutoDownload.Headless.csproj TelegramAutoDownload.Headless/
RUN dotnet restore TelegramAutoDownload.Headless/TelegramAutoDownload.Headless.csproj

# Copy the rest of the source and publish
COPY BasePlugin/                              BasePlugin/
COPY Logger/                                  Logger/
COPY TelegramClient/                          TelegramClient/
COPY DownloadService/                         DownloadService/
COPY YoutubePlugin/                           YoutubePlugin/
COPY SocialMediaPlugin/                       SocialMediaPlugin/
COPY TorrentPlugin/                           TorrentPlugin/
COPY TelegramAutoDownload.Headless/           TelegramAutoDownload.Headless/

RUN dotnet publish TelegramAutoDownload.Headless/TelegramAutoDownload.Headless.csproj \
    -c Release -o /out --no-self-contained

# ── Runtime stage ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      yt-dlp ffmpeg ca-certificates \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /out .

# All user-writable state under /data so it survives container restarts.
# SpecialFolder.ApplicationData on Linux honours XDG_CONFIG_HOME → puts session.dat in /data/TelegramAutoDownload/
ENV DATA_DIR=/data \
    DOWNLOADS_DIR=/downloads \
    XDG_CONFIG_HOME=/data \
    LISTEN_URL=http://0.0.0.0:8080 \
    ASPNETCORE_URLS=http://0.0.0.0:8080

VOLUME ["/data", "/downloads"]
EXPOSE 8080

ENTRYPOINT ["dotnet", "TelegramAutoDownload.Headless.dll"]
