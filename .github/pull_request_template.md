<!-- Titulo: SCRUM-NN HU-NN <Titulo corto>: <resumen> (Unit N/Integration N/Security N, critic PASS) -->

## Que cambia y por que

- Spec: `specs/...` (enlace)
- Jira: SCRUM-NN
- PR de la contraparte en musubi-app (si aplica): #

## Checklist

- [ ] Titulo con la clave Jira (`SCRUM-NN ...`) y rama `feature/SCRUM-NN-slug` o `fix/SCRUM-NN-slug`
- [ ] `dotnet build Musubi.slnx -c Release` 0 errores / 0 advertencias
- [ ] Unit, Integration, Security y Contract verdes con `--no-build` (conteos reales en el titulo)
- [ ] Sin secretos (appsettings solo con placeholders; secretos en user-secrets o archivos ignorados)
- [ ] Sin `TODO`: solo `NOTE (NN-NN)` / `NOTE (HU-NN)` con su spec duena registrada en `task.md`
- [ ] Cada endpoint nuevo con auth explicita (`[Authorize]` / `[AllowAnonymous]`) y fila en `docs/endpoints.md` (`check_endpoints` OK)
- [ ] `pwsh scripts/critic-guardrails.ps1` PASS
- [ ] `spec.md`, `plan.md` y `task.md` conciliados con lo implementado; `docs/trazabilidad.md` actualizado
- [ ] `Memoria.md` actualizada (que, decisiones con su porque, errores a evitar)
