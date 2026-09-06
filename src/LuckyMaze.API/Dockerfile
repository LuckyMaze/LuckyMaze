# Build context is the repository root, because LuckyMaze.API references the other three
# projects under src/ - restore needs all of their .csproj files present, not just this one.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LuckyMaze.API.dll"]
