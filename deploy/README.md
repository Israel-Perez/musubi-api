# deploy

Infraestructura de musubi-api.

| Archivo | Uso | Estado |
|---|---|---|
| `docker-compose.local.yml` | MongoDB 8.0.32 local para desarrollo | ✅ Escrito; verificado por revisión (no hay Docker en la máquina donde se creó la plantilla) |
| `.env.example` | Plantilla de `deploy/.env` (ignorado) con `MONGO_PASSWORD` | ✅ |
| MongoDB Atlas M0 | Base de la nube (servicio externo permitido por los lineamientos) | NOTE (02-05) |
| AWS (ALB + 2 EC2 + CloudFront, Terraform) | Despliegue | NOTE (02-05) |

## MongoDB local

```powershell
Copy-Item deploy/.env.example deploy/.env     # y cambia la contraseña
docker compose -f deploy/docker-compose.local.yml --env-file deploy/.env up -d
dotnet user-secrets --project Musubi.Api set "UseInMemoryDatabase" "false"
dotnet user-secrets --project Musubi.Api set "ConnectionStrings:Mongo" "mongodb://musubi:<la de deploy/.env>@localhost:27017/?authSource=admin"
dotnet run --project Musubi.Api
```

Comprueba la conexión con `curl http://localhost:5080/health/ready` → `Healthy` (el check `database` hace `ping` a MongoDB).

## MongoDB Atlas (nube)

La cadena `mongodb+srv://…` de Atlas es un **secreto**: va en user-secrets o en las variables del despliegue, nunca en un appsettings versionado (lo impiden `AppSettingsTests`, el critic B2 y Semgrep). En Atlas, el usuario de la app solo tiene `readWrite` sobre la base `musubi` y la lista de IP se limita a la salida de las instancias (02-05).

La imagen de la API se construye con el `Dockerfile` de la raíz; el job `docker-build` del CI la verifica en cada PR.
