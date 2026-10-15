---
name: docker-multistage
description: Construir la imagen de musubi-api de forma reproducible y segura (multi-stage SDK → aspnet, versiones fijadas, usuario no root, sin secretos). Úsala al modificar el Dockerfile, el .dockerignore o el compose local.
---

## Propósito
Una imagen pequeña, reproducible y sin secretos para AWS (`02-05`).

## Cuándo usarla
Al tocar `Dockerfile`, `.dockerignore` o `deploy/docker-compose.local.yml`, o al actualizar el SDK o el runtime.

## Precondiciones
Versiones verificadas en MCR (`mcr.microsoft.com/v2/dotnet/{sdk,aspnet}/tags/list`) y en Docker Hub (postgres).

## Pasos
1. **Stage `build`:** `mcr.microsoft.com/dotnet/sdk:<versión exacta>`; copiar primero `global.json`, `nuget.config`, `Directory.Build.props` y el `.csproj` → `dotnet restore` (capa cacheable) → copiar el código → `dotnet publish -c Release --no-restore /p:UseAppHost=false`.
2. **Stage `runtime`:** `mcr.microsoft.com/dotnet/aspnet:<versión exacta>`, `ASPNETCORE_HTTP_PORTS=8080`, `USER $APP_UID` (usuario no root de las imágenes oficiales), `EXPOSE 8080`.
3. **`.dockerignore`** deja fuera `.git`, las pruebas, `specs/`, `docs/`, `bin/`, `obj/`, `appsettings*.Local.json`, `.env` y las llaves.
4. **Secretos:** nunca con `COPY`, `ARG` ni `ENV`; llegan en runtime por variables o secretos del orquestador.
5. **Verificar:** job `docker-build` del CI (`docker build --pull -t musubi-api:ci .`, sin push).
6. **Compose local:** PostgreSQL con versión fija, puerto solo en `127.0.0.1`, contraseña desde `deploy/.env` (ignorado) con `${POSTGRES_PASSWORD:?…}`, volumen en `/var/lib/postgresql` (formato de PostgreSQL 18+).

## Checklist
Versiones fijadas · usuario no root · sin secretos en capas · `.dockerignore` defensivo · Dependabot (`docker`) activo.

## Criterios de done
Job `docker-build` en verde; `docker history` sin secretos; la imagen arranca y `/health` responde 200 (verificación local cuando haya Docker).

## Límites/trampas
- La imagen `aspnet` no trae `curl` ni `wget`: el health check del contenedor debe hacerse desde el orquestador o con una imagen que los incluya.
- La máquina donde se creó la plantilla no tiene Docker: la primera verificación real del Dockerfile es el CI.
- El escaneo de la imagen (Trivy) llega en `02-05`.

## Referencias
`Dockerfile`, `.dockerignore`, `deploy/`, `.github/dependabot.yml`.
