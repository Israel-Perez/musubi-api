# 01-04 — Referencia de configuración (tareas)

## T1 — Tabla canónica y appsettings sin secretos
- **Crear:** los 4 `appsettings*.json`, `Properties/launchSettings.json` (puerto 5080, el que usa el emulador del front: `http://10.0.2.2:5080`), `UnitTest/Common/AppSettingsTests.cs`.
- **Verificar:** `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build` → 19/19; critic PASS.
- **Estado:** ✅ Hecho (15-Oct-2026).

## Pendientes
- Claves `Jwt:*` → HU-02; `Firebase:*` → HU-31; `GenAi:*` → HU-32; `Payments:*` → HU-55 (HU-43 fuera de alcance); `AllowedHosts` real → 02-05.
