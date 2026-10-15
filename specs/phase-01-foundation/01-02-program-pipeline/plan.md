# plan.md — 01-02 Pipeline y DI

1. **Componentes:** `Program.cs`, `Platform/Hosting/PlatformServiceCollectionExtensions.cs`, `Platform/Hosting/PlatformPipelineExtensions.cs`.
2. **Decisiones:**
   - Registro y pipeline en extensiones: `Program.cs` queda en ~10 líneas y cada módulo agrega su `Add<Modulo>Module` sin tocar el orden.
   - CORS con origen, métodos y headers explícitos; lista vacía por defecto (la app Android no lo necesita).
   - `Asp.Versioning.OpenApi` para tener un documento OpenAPI por versión (resuelve AV0029/AV0030 sin suprimirlos).
3. **Guardarraíles:** HSTS solo fuera de Dev; S5122 en el build contra CORS abierto; sin `try/catch` en controllers.
4. **Validación:** Unit (DI), Integration (pipeline en Dev), Security (HSTS y CORS en Production) + self-test que quita `UseHsts`.
5. **Secuencia:** servicios → pipeline → pruebas → self-test → spec conciliada.
