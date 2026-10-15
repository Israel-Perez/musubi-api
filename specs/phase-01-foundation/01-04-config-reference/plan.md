# plan.md — 01-04 Referencia de configuración

1. **Componentes:** `appsettings{,.Development,.Production,.Example}.json`, `UserSecretsId` en el csproj, carga de `appsettings.Local.json` solo en Dev, `UnitTest/Common/AppSettingsTests.cs`.
2. **Decisiones:**
   - En la base, `UseInMemoryDatabase=false` (fail-fast si falta la BD fuera de Dev); en Development, `true`, para que `dotnet run` funcione sin PostgreSQL.
   - La connection string de ejemplo va **sin** password; la completa va en user-secrets.
3. **Guardarraíles:** pruebas que rechazan secretos en los 4 archivos versionados; critic B2b; regla de Semgrep.
4. **Validación:** Unit 19/19 + critic PASS + Semgrep 0.
5. **Secuencia:** archivos → tabla canónica → pruebas.
