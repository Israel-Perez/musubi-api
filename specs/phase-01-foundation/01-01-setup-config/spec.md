# 01-01 — Setup y configuración

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) · contraparte: `musubi-app:01-01`

## Contexto
Deja compilando la solución base de musubi-api con análisis estricto y restauración segura de paquetes. **Depende de**: `00-01`, `00-02`.

## Requisitos
1. Solución `Musubi.slnx` (formato XML `.slnx`) con `Musubi.Api` y las 5 suites de pruebas.
2. `Directory.Build.props` con `AnalysisMode=All`, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `Nullable`, `NuGetAudit` (modo `all`) y `SonarAnalyzer.CSharp`.
3. `nuget.config` con una sola fuente (nuget.org) y Package Source Mapping explícito (sin comodín `*`).
4. `global.json` que fije la banda del SDK 10.0.4xx y el runner de pruebas Microsoft.Testing.Platform.
5. `dotnet restore` y `dotnet build -c Release` sin errores ni advertencias.

## Diseño
- Solución `Musubi.slnx` en la raíz; los proyectos de prueba se ejecutan con xUnit v3 sobre Microsoft.Testing.Platform (`OutputType=Exe`).
- `GenerateDocumentationFile=true` para que IDE0005 (usings sin usar) se aplique en el build; las pruebas suprimen CS1591, CA1515 y CA1707 con justificación en su `.csproj`.
- `Musubi.Api.csproj` suprime CA1515 (MVC solo descubre controllers públicos y System.Text.Json serializa DTOs públicos).
- Versiones exactas en los `.csproj` (registradas en Memoria.md, 15-Oct-2026).

## Contratos
- `global.json`: `sdk.version=10.0.400`, `rollForward=latestFeature`, `test.runner=Microsoft.Testing.Platform`.
- Patrones de `nuget.config`: `Asp.Versioning.*`, `coverlet.*`, `DnsClient`, `Microsoft.*`, `MongoDB.*`, `Scalar.*`, `SharpCompress`, `Snappier`, `SonarAnalyzer.*`, `System.*`, `xunit.*`, `ZstdSharp.Port` (`DnsClient`, `SharpCompress`, `Snappier` y `ZstdSharp.Port` son transitivos de `MongoDB.Driver`; 20-Oct-2026).

## Tests
- `UnitTest/Common/BuildSmokeTests.cs` (2): el ensamblado carga sin errores de tipo y contiene `Program`.

## Criterios
- `dotnet build Musubi.slnx -c Release` → 0 errores, 0 advertencias.
- `dotnet restore Musubi.slnx --force --no-cache -p:RestorePackagesPath=<carpeta vacía>` → sin NU1100 (70 paquetes, 15-Oct-2026).
- `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build` → `BuildSmokeTests` en verde.

## Límites
No cubre el pipeline (01-02), la salud (01-03) ni la configuración (01-04). La versión del SDK en el CI sale de `global.json` (`setup-dotnet` con `global-json-file`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 15-Oct-2026
- **Detalle:** Desviación respecto de la referencia: xUnit v3 + Microsoft.Testing.Platform en lugar de xUnit 2.9 + VSTest (lección 00-04 #16); Source Mapping explícito y verificado con caché vacía.
