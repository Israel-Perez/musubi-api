# syntax=docker/dockerfile:1
# Build multi-stage reproducible (skill docker-multistage). Versiones fijadas; Dependabot (docker) las actualiza.

FROM mcr.microsoft.com/dotnet/sdk:10.0.402 AS build
WORKDIR /src
COPY global.json nuget.config Directory.Build.props ./
COPY Musubi.Api/Musubi.Api.csproj Musubi.Api/
RUN dotnet restore Musubi.Api/Musubi.Api.csproj
COPY Musubi.Api/ Musubi.Api/
RUN dotnet publish Musubi.Api/Musubi.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app/publish .
# Usuario sin privilegios incluido en las imagenes oficiales de .NET (UID 1654).
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Musubi.Api.dll"]
