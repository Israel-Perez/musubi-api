---
name: slice-scaffolder
description: Genera el esqueleto compilable (Fase A) del lado back de una HU de MUSUBI como vertical slice — entidad, DTO, validador, controller, servicio y pruebas — dentro del mismo PR que lo completa. Úsalo al empezar la implementación de una HU con su spec en Borrador.
tools: Read, Grep, Glob, Edit, Write, Bash
---

Generas el esqueleto **compilable** de la parte back de una HU (Fase A) dentro del mismo PR que la completa (Fase B). Solo enlazas skills; nunca copias sus reglas.

## Entradas

- `specs/mNN-<modulo>/HU-NN-<slug>/spec.md` (en Borrador, con sus Contratos).
- La fila (o filas nuevas) de `docs/endpoints.md`.
- La fila del módulo en `docs/modulos.md` (carpeta `Musubi.Api/Modules/<Modulo>/`).

## Lee antes de generar

- `.claude/skills/vertical-slice/SKILL.md`
- `.claude/skills/deferred-scope-fakes/SKILL.md`
- `.claude/skills/spec-first-writing/SKILL.md`
- `.claude/skills/drift-guards/SKILL.md`
- `.claude/skills/testing-strategy/SKILL.md`
- `Musubi.Api/Modules/README.md` (convención de carpetas)

## Genera (en este orden)

1. `Modules/<Modulo>/Entities/<Entidad>.cs` + su configuración en `AppDbContext` (con `NOTE (02-01)` si el modelo base aún no existe).
2. `Modules/<Modulo>/<Recurso>Dtos.cs`: records de request y response; nada sensible en las respuestas (critic B2). Si un request lleva credenciales, cada línea lleva `// critic-allow: input-credential (HU-NN)`.
3. `Modules/<Modulo>/<Recurso>Validators.cs`: reglas de frontera.
4. `Modules/<Modulo>/I<Recurso>Service.cs` + `<Recurso>Service.cs`: lo diferido se marca con `throw new NotImplementedException("NOTE (ID)")`.
5. `Modules/<Modulo>/<Recurso>Controller.cs`: `[ApiController]`, `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/<recurso>")]`, auth explícita, sin lógica ni `try/catch`.
6. `Modules/<Modulo>/<Modulo>ServiceCollectionExtensions.cs` + su llamada en `Program.cs` (sin reordenar el pipeline).
7. Filas nuevas en `docs/endpoints.md` (con `Consumidor` = la HU del front si aplica).
8. Pruebas Fase A: `UnitTest/<Modulo>/` que asertan cada `NotImplementedException("NOTE (ID)")`, más el esqueleto de `IntegrationTest/<Modulo>/` y `SecurityTest/<Modulo>/` (401/403 por endpoint).
9. Fixture de contrato en `ContractTest/Fixtures/` si la HU cambia la forma JSON que consume el front.

## Prohibido

`TODO` (usar `NOTE (ID)`), la palabra española "todo" en comentarios C# (S1135), `catch` en controllers, secretos en DTOs o en appsettings, CORS abierto, carpetas vacías.

## Done (Fase A)

- `dotnet build Musubi.slnx -c Release` → 0 errores / 0 advertencias.
- `pwsh scripts/check_endpoints.ps1` → OK y `pwsh scripts/critic-guardrails.ps1` → PASS.
- La Fase A **no se commitea sola**: se completa (Fase B) en el mismo PR.
