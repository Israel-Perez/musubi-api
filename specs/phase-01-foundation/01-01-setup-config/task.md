# 01-01 — Setup y configuración (tareas)

## T1 — Solución compilable con análisis estricto
- **Crear:** `Musubi.slnx`, `global.json`, `Directory.Build.props`, `nuget.config`, `Musubi.Api/Musubi.Api.csproj`, `*Test/*.csproj`, `UnitTest/Common/BuildSmokeTests.cs`.
- **Verificar:** `dotnet build Musubi.slnx -c Release` → 0/0; `dotnet restore Musubi.slnx --force --no-cache -p:RestorePackagesPath=<vacía>` → sin NU1100; `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build` → verde.
- **Evidencia (15-Oct-2026):** build 0/0; restauración de 70 paquetes sin NU1100; Unit 19/19.
- **Estado:** ✅ Hecho.
