# deploy

Infraestructura de musubi-api.

| Archivo | Uso | Estado |
|---|---|---|
| `docker-compose.local.yml` | PostgreSQL 18.6 local para desarrollo | ✅ Escrito; verificado por revisión (no hay Docker en la máquina donde se creó la plantilla) |
| `.env.example` | Plantilla de `deploy/.env` (ignorado) con `POSTGRES_PASSWORD` | ✅ |
| AWS (API, base administrada, imágenes) | Despliegue | NOTE (02-05) |

## PostgreSQL local

```powershell
Copy-Item deploy/.env.example deploy/.env     # y cambia la contraseña
docker compose -f deploy/docker-compose.local.yml --env-file deploy/.env up -d
dotnet user-secrets --project Musubi.Api set "UseInMemoryDatabase" "false"
dotnet user-secrets --project Musubi.Api set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=musubi;Username=musubi;Password=<la de deploy/.env>"
dotnet run --project Musubi.Api
```

Comprueba la conexión con `curl http://localhost:5080/health/ready` → `Healthy`.
La imagen de la API se construye con el `Dockerfile` de la raíz; el job `docker-build` del CI la verifica en cada PR.
