# Build context = repo root (docker-compose builds from .)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first for layer caching (Server references Core)
COPY src/MahjongTutor.Core/MahjongTutor.Core.csproj src/MahjongTutor.Core/
COPY src/MahjongTutor.Server/MahjongTutor.Server.csproj src/MahjongTutor.Server/
RUN dotnet restore src/MahjongTutor.Server/MahjongTutor.Server.csproj

COPY src/ src/
RUN dotnet publish src/MahjongTutor.Server/MahjongTutor.Server.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "MahjongTutor.Server.dll"]
