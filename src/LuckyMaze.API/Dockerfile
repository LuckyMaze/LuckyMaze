# Build context is the repository root, because LuckyMaze.API references the other three .NET
# projects under src/ - restore needs all of their .csproj files present, not just this one - and
# because the frontend stage below needs src/LuckyMaze.Frontend too.

ARG BUN_VERSION=1.3.13
# --platform=$BUILDPLATFORM: this stage's output (plain JS/CSS) isn't architecture-specific, so it
# only needs to run once, natively, on whatever's actually running the build - not once per target
# platform under QEMU emulation. Cuts a multi-arch build from minutes to seconds for this stage.
FROM --platform=$BUILDPLATFORM oven/bun:${BUN_VERSION} AS frontend-build
WORKDIR /src/frontend
COPY src/LuckyMaze.Frontend/package.json src/LuckyMaze.Frontend/bun.lock ./
RUN bun install --frozen-lockfile
COPY src/LuckyMaze.Frontend .
RUN bun run build

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Same reasoning as frontend-build above: this project is framework-dependent (no -r runtime
# identifier, UseAppHost=false), so the published output is plain portable IL, not
# architecture-specific - build it once, natively, instead of once per target platform.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/LuckyMaze.API/LuckyMaze.API.csproj", "LuckyMaze.API/"]
COPY ["src/LuckyMaze.Application/LuckyMaze.Application.csproj", "LuckyMaze.Application/"]
COPY ["src/LuckyMaze.Domain/LuckyMaze.Domain.csproj", "LuckyMaze.Domain/"]
COPY ["src/LuckyMaze.Infrastructure/LuckyMaze.Infrastructure.csproj", "LuckyMaze.Infrastructure/"]
RUN dotnet restore "LuckyMaze.API/LuckyMaze.API.csproj"
COPY src/LuckyMaze.API/ LuckyMaze.API/
COPY src/LuckyMaze.Application/ LuckyMaze.Application/
COPY src/LuckyMaze.Domain/ LuckyMaze.Domain/
COPY src/LuckyMaze.Infrastructure/ LuckyMaze.Infrastructure/
WORKDIR "/src/LuckyMaze.API"
RUN dotnet build "LuckyMaze.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "LuckyMaze.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
COPY --from=publish /app/publish .
# Angular's build always nests the actual static output one level down, under browser/ - see
# https://angular.dev/tools/cli/build-system-migration.
COPY --from=frontend-build /src/frontend/dist/LuckyMaze.Frontend/browser ./wwwroot
ENTRYPOINT ["dotnet", "LuckyMaze.API.dll"]
