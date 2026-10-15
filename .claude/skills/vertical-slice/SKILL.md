---
name: vertical-slice
description: Entregar el lado back de una HU de MUSUBI como un slice vertical completo (entidad, DTO, validador, servicio, controller, fila del catálogo y pruebas) dentro de Musubi.Api/Modules/<Modulo>. Úsala al implementar cualquier endpoint.
---

## Propósito
Que cada HU se entregue completa y conectada en un solo PR, dentro de su módulo, sin PRs por capa.

## Cuándo usarla
En cada HU que agregue o cambie endpoints, y al revisar si un PR está completo.

## Precondiciones
- Spec de la HU en Borrador con sus Contratos (skill `spec-first-writing`).
- Módulo y carpeta definidos en `docs/modulos.md`.

## Pasos
1. **Entidad** en `Modules/<Modulo>/Entities/` + su configuración en `AppDbContext` (comunidad/universidad como dato: RNF-21).
2. **DTOs** (records) de request/response; las respuestas nunca exponen hashes, tokens ni PII de terceros (critic B2, OWASP API3).
3. **Validadores** de frontera (longitudes, rangos, formatos; correo institucional en HU-03).
4. **Servicio** detrás de `I<Recurso>Service`; verifica la propiedad del recurso (RNF-03, OWASP API1); el trabajo pesado va en segundo plano (RNF-11).
5. **Controller** en `api/v{version:apiVersion}/<recurso>` con auth explícita, sin lógica ni `try/catch`; códigos correctos (201 + `CreatedAtAction`, 204, 400, 401, 403, 404, 409).
6. **Registro** con `Add<Modulo>Module` en `Program.cs`, sin reordenar el pipeline (01-02).
7. **Catálogo:** fila en `docs/endpoints.md` con auth, spec, DTOs, códigos y consumidor → `pwsh scripts/check_endpoints.ps1`.
8. **Pruebas:** Unit (reglas del servicio y validadores) + Integration (WAF, flujo feliz y errores) + Security (401 sin token, 403 si no es dueño) + Contract (fixture si el front consume la forma JSON).
9. **Conciliar** `spec/plan/task`, `docs/trazabilidad.md` y `Memoria.md`.

## Checklist
Los 6 componentes presentes y conectados + fila del catálogo + 3 suites con pruebas del slice + fixture (si aplica).

## Criterios de done
- `dotnet build Musubi.slnx -c Release` 0/0.
- Unit, Integration, Security y Contract verdes con `--no-build`.
- `critic` PASS, `endpoints` OK, `security-reviewer` PASS.

## Límites/trampas
- No dividir el slice en PRs por capa; no commitear la Fase A (esqueleto) sin su Fase B.
- No tocar las tablas de otro módulo: usar su interfaz pública.
- Un `catch` en el controller es bloqueante (B4): el error se lanza y lo maneja el pipeline/contrato de errores (`02-03`).

## Referencias
`Musubi.Api/Modules/README.md`, agente `slice-scaffolder`, skills `deferred-scope-fakes`, `drift-guards`, `testing-strategy`, `critic-guardrails`.
