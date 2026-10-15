# plan.md — 01-01 Setup y configuración

1. **Componentes:** `Musubi.slnx`, `global.json`, `Directory.Build.props`, `nuget.config`, `Musubi.Api/Musubi.Api.csproj`, los 5 `.csproj` de pruebas.
2. **Decisiones:**
   - xUnit v3 sobre Microsoft.Testing.Platform: es la línea mantenida de xUnit y el modo nativo de `dotnet test` en .NET 10 (xUnit 2.9 está en mantenimiento).
   - `NuGetAuditMode=all`: audita también las dependencias transitivas.
   - Source Mapping con patrones explícitos derivados de `project.assets.json`, no con `*`.
3. **Guardarraíles:** advertencias = errores; ningún paquete fuera del stack (00-01); restauración verificada con caché vacía.
4. **Validación:** build Release 0/0 + `BuildSmokeTests` + restauración con caché vacía.
5. **Secuencia:** props → csproj → slnx → build → derivar los patrones de Source Mapping → restaurar con caché vacía.
