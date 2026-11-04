# Build context = repo root (docker-compose builds from .)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Full source first: static-web-assets (_framework/blazor.web.js) are only
# resolved into the publish output when the files exist at restore time.
COPY src/ src/
RUN dotnet publish src/MahjongTutor.Server/MahjongTutor.Server.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "MahjongTutor.Server.dll"]
