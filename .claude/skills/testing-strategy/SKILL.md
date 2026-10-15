---
name: testing-strategy
description: Qué suite de musubi-api cubre qué (Unit, Integration, Security, Contract, Database), reglas anti-flakiness y comandos con xUnit v3 sobre Microsoft.Testing.Platform. Úsala al planear las pruebas de una HU o al auditar cobertura.
---

## Propósito
Que cada tipo de riesgo tenga su suite, con pruebas deterministas y conteos reales.

## Cuándo usarla
Al escribir la sección `Tests` de una spec, al crear pruebas y al auditar.

## Precondiciones
Build Release 0/0 (`dotnet build Musubi.slnx -c Release`).

## Pasos
1. **Matriz suite → responsabilidad:**

| Suite | Cubre | Herramientas |
|---|---|---|
| `UnitTest/` | Reglas de servicios y validadores, registro DI, configuración sin secretos | xUnit v3 |
| `IntegrationTest/` | Flujos HTTP completos con WebApplicationFactory + InMemory | `Microsoft.AspNetCore.Mvc.Testing` |
| `SecurityTest/` | 401/403, BOLA (RNF-03), HSTS, CORS, errores sin detalle, auth explícita | WebApplicationFactory por ambiente (`ProductionApiFactory`, `DevelopmentApiFactory`) |
| `ContractTest/` | Forma JSON (camelCase + tipos) contra `Fixtures/*.json`: lo que consume `musubi-app` | WebApplicationFactory |
| `DatabaseTest/` | Migraciones, restricciones e índices contra PostgreSQL real | Testcontainers — `NOTE (02-01)` |

2. **Comandos:** `dotnet test --project <Suite>/<Suite>.csproj -c Release --no-build` (en modo MTP no se acepta la ruta posicional).
3. **Anti-flakiness:** `xunit.runner.json` con `parallelizeTestCollections: false` en las suites WAF (comparten la base InMemory `musubi`); datos únicos por prueba (`Guid`); nada de `TotalCount == 1` exacto en stores compartidos.
4. **Cancelación:** pasar `TestContext.Current.CancellationToken` a las llamadas async (analizador xUnit1051).
5. **Frontera exacta:** probar `>=`/`<`, los mensajes y ambas ramas (00-04 #6).
6. **Grupo de control:** cuando una prueba asegura una ausencia (p. ej. sin detalle de error en Production), otra prueba confirma la presencia en el caso contrario (Development).
7. **HSTS en el wire:** usar un host que no sea localhost (`https://api.musubi.test`); el middleware excluye localhost (00-04 #19).
8. **RNF-25:** las HU de M-01 (auth), M-06 (coincidencias) y M-08 (pedidos) llevan Unit + Integration + Security obligatoriamente.

## Checklist
Matriz respetada · runner en serie en las suites WAF · CancellationToken · conteos reales en spec/PR.

## Criterios de done
Las 4 suites en verde con `--no-build`; conteos citados en la spec y en el título del PR.

## Límites/trampas
- No subir el paralelismo sin aislar la base InMemory.
- Cobertura: `coverlet.MTP` está instalado, pero el umbral es "🎯 Por medir" (`02-06`); no usar la cobertura de línea como único gate.

## Referencias
`00-03`, `00-04`, `IntegrationTest/Common/MusubiApiFactory.cs`, `SecurityTest/Common/SecurityApiFactory.cs`.
