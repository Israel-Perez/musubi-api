# Modules — convención de los módulos M-01…M-14

Cada módulo de [`docs/modulos.md`](../../docs/modulos.md) vive en su carpeta `Modules/<Modulo>/`, creada con la **primera HU** del módulo (no hay carpetas vacías). Un módulo es un *vertical slice* (skill `vertical-slice`):

```
Modules/<Modulo>/
├── <Recurso>Controller.cs      # [ApiController] + [ApiVersion("1.0")] + auth explícita; sin lógica ni try/catch; errores con this.Problem(<Modulo>Errors.X)
├── <Modulo>Errors.cs           # ApiError del módulo (código, HTTP, mensaje); fila en docs/errores.md
├── <Recurso>Dtos.cs            # records de request/response (camelCase en el wire); sin secretos en las respuestas
├── <Recurso>Validators.cs      # validación de entrada en la frontera
├── I<Recurso>Service.cs        # interfaz pública del módulo (otros módulos solo usan esto)
├── <Recurso>Service.cs         # reglas de negocio
├── Data/                       # documentos, contratos de repositorio, Mongo* e InMemory* (misma batería en TestShared/)
└── <Modulo>ServiceCollectionExtensions.cs   # services.Add<Modulo>Module(configuration)
```

Reglas:

- Cada endpoint nuevo lleva su fila en `docs/endpoints.md` (lo verifica `scripts/check_endpoints.ps1`).
- Un módulo no lee las colecciones de otro: usa su `I<Recurso>Service` o un evento.
- Los proveedores externos (IA, ML, moderación, FCM) van detrás de una interfaz del módulo, con un fake para pruebas y degradación controlada (constitución §2, principio 3).
- Se registra en `Program.cs` mediante su método de extensión, sin tocar el orden del pipeline (01-02).
