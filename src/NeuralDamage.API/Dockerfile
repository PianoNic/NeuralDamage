FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/NeuralDamage.API/NeuralDamage.API.csproj", "NeuralDamage.API/"]
COPY ["src/NeuralDamage.Application/NeuralDamage.Application.csproj", "NeuralDamage.Application/"]
COPY ["src/NeuralDamage.Domain/NeuralDamage.Domain.csproj", "NeuralDamage.Domain/"]
COPY ["src/NeuralDamage.Infrastructure/NeuralDamage.Infrastructure.csproj", "NeuralDamage.Infrastructure/"]
RUN dotnet restore "NeuralDamage.API/NeuralDamage.API.csproj"
COPY src/ .
WORKDIR "/src/NeuralDamage.API"
RUN dotnet build -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "NeuralDamage.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Frontend build
FROM oven/bun:1 AS frontend-build
WORKDIR /src/frontend
RUN bun add -g @angular/cli@21
COPY src/ArgonFetch.Frontend/package*.json bunfig.toml* bun.lock* ./
RUN bun install --frozen-lockfile
COPY src/ArgonFetch.Frontend .
RUN bun run build:agent --output-path /.artifacts/frontend

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=frontend-build /.artifacts/frontend ./wwwroot
ENTRYPOINT ["dotnet", "NeuralDamage.API.dll"]