FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY application.properties /application.properties
COPY ["src/Directory.Build.props", "."]
COPY ["src/NeuralDamage.API/NeuralDamage.API.csproj", "NeuralDamage.API/"]
COPY ["src/NeuralDamage.Application/NeuralDamage.Application.csproj", "NeuralDamage.Application/"]
COPY ["src/NeuralDamage.Domain/NeuralDamage.Domain.csproj", "NeuralDamage.Domain/"]
COPY ["src/NeuralDamage.Infrastructure/NeuralDamage.Infrastructure.csproj", "NeuralDamage.Infrastructure/"]
RUN dotnet restore "NeuralDamage.API/NeuralDamage.API.csproj"
COPY src/ .
WORKDIR "/src/NeuralDamage.API"

# Publish compiles on its own; a separate dotnet build first only compiled everything twice.
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
# Published for the image's own platform, so only its native libraries (Skia, HarfBuzz) are copied,
# not the ones for every other OS and CPU.
RUN case "$TARGETARCH" in arm64) rid=linux-arm64 ;; *) rid=linux-x64 ;; esac \
    && dotnet publish "NeuralDamage.API.csproj" -c $BUILD_CONFIGURATION -r $rid --self-contained false -o /app/publish /p:UseAppHost=false

# Frontend build
FROM node:24-slim AS frontend-build
COPY --from=oven/bun:1.3.5 /usr/local/bin/bun /usr/local/bin/bun
WORKDIR /src/frontend
COPY src/NeuralDamage.Frontend/package.json src/NeuralDamage.Frontend/bun.lock ./
RUN bun install --frozen-lockfile
COPY src/NeuralDamage.Frontend .
RUN bun run build

# Deno is the JavaScript runtime yt-dlp needs for YouTube.
FROM denoland/deno:bin-2.9.8 AS deno

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=frontend-build /src/frontend/dist/NeuralDamage.Frontend/browser ./wwwroot
# Uploaded images (Attachments:Path). Created here, owned by the app user, so a
# volume mounted on it starts out writable.
USER root
RUN mkdir -p /app/data/attachments && chown -R $APP_UID /app/data
# yt-dlp fetches whole songs for music on posts and stories (Music:YtDlpPath); without it songs play
# Deezer's 30-second preview. Its standalone build needs no Python.
ARG YTDLP_VERSION=2026.08.19
ADD --chmod=755 https://github.com/yt-dlp/yt-dlp/releases/download/${YTDLP_VERSION}/yt-dlp_linux /usr/local/bin/yt-dlp
COPY --from=deno /deno /usr/local/bin/deno
USER $APP_UID
ENTRYPOINT ["dotnet", "NeuralDamage.API.dll"]