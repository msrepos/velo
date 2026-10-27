FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props global.json ./
COPY src/ src/
RUN dotnet restore src/Velo.Api/Velo.Api.csproj
RUN dotnet publish src/Velo.Api/Velo.Api.csproj --configuration Release --no-restore --output /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /out ./
USER $APP_UID
ENTRYPOINT ["dotnet", "Velo.Api.dll"]
