# 02-09 — Pruebas de mutación y de rendimiento

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) / SCRUM-94 / S4 / Plataforma / —

## Contexto
La plantilla `WebAPIDevSecOps` de la materia tiene 8 proyectos. A `musubi-api` le faltaban `MutationTest` y `PerformanceTest`, que se habían dejado para después en la fundación (`00-03`).

**Depende de:** `01-05` (suites y CI) y `00-03` (RNF-09: tiempos de respuesta).

## Requisitos
1. **Mutación:** Stryker.NET mide si las pruebas detectan cambios en las reglas del núcleo. El puntaje mínimo es 70 % (`break`).
2. **Rendimiento:** el p95 de las rutas públicas más usadas (`ping`, categorías y vitrina) se mide bajo carga concurrente:
   - 400 peticiones por ruta, 20 a la vez;
   - en el CI, en proceso y con umbral;
   - contra AWS, a mano con `PERF_BASE_URL`.
3. **Mediciones reales:** los umbrales salen de medir, no de suponer (skill `empirical-verification`).

## Diseño
- **`MutationTest/` (xUnit v2 + VSTest):**
  - Stryker.NET 5 no soporta Microsoft.Testing.Platform ([stryker-net#3094](https://github.com/stryker-mutator/stryker-net/issues/3094)). Además, activa cada mutante desde un colector dentro del proceso de VSTest, y xUnit v3 corre siempre en un proceso aparte. Medido el 03-Nov-2026: con las pruebas de `UnitTest`, los 88 mutantes "sobreviven" porque nunca se activan.
  - Por eso el proyecto usa xUnit v2 y tiene pruebas propias sobre reglas puras: `PasswordPolicy`, `PasswordHashing` y `EmailAddresses` (`stryker-config.json`, `mutate`).
  - No entra en `Musubi.slnx`. Stryker es una herramienta local (`dotnet-tools.json`).
- **`PerformanceTest/` (xUnit v3, como las demás suites):** `LatencyTests`, con una `WebApplicationFactory` en memoria. Mide la API, no la red ni MongoDB.
- **CI:**
  - job `performance` en `ci-pr.yml`;
  - workflow `mutation.yml` (manual y cada lunes; tarda varios minutos), que deja el reporte HTML como artefacto.

## Contratos
- **Variables de `PerformanceTest`:** `PERF_BASE_URL` (por ejemplo, `https://api.musubiapp.com.mx`) y `PERF_P95_MS` (umbral en milisegundos).
- **Umbrales de Stryker:** `high` 80, `low` 70, `break` 70.

## Tests
- `MutationTest/AuthRulesTests.cs` (22 casos).
- `PerformanceTest/LatencyTests.cs` (3 rutas).

## Criterios
- [x] Puntaje de mutación de 87.5 % (28 mutantes muertos, 0 sobrevivientes y 4 sin cobertura en el señuelo de `VerifyAgainstDecoy`), por encima de 70 %.
- [x] **p95 en proceso** (03-Nov-2026, 2 corridas): `ping` 1.1 ms, categorías 2.7 ms y vitrina 4.6 ms. El umbral del CI es de 100 ms.
- [x] **p95 contra AWS** (03-Nov-2026, API `0.4.0-alpha.1`, sin la caché de `02-02`): `ping` 127 ms, categorías 895 ms y vitrina 994 ms (p50 de 908 ms).
- [ ] Después de desplegar `02-02` (caché), medir otra vez contra AWS y fijar RNF-09.

## Límites
- **La mutación cubre solo reglas puras del núcleo.** Ampliarla a los servicios exige pruebas en xUnit v2, o esperar a que Stryker soporte MTP (`NOTE (02-09)`).
- **El umbral contra AWS** (`PERF_P95_MS`) se decide con el PO al cerrar RNF-09.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 03-Nov-2026
- **Detalle:**
  - completa los 8 proyectos de la plantilla `WebAPIDevSecOps`;
  - Stryker con xUnit v2 por la limitación de MTP (medida).
