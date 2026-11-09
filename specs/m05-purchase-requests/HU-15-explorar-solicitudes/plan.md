# HU-15 — Explorar solicitudes publicadas (plan)

## Decisiones
- **Una ruta pública con sesión opcional:** `GET /wanted` sirve a `Se-busca` y a `Se-busca-visitante`, como `GET /products` (HU-48). Con token manda su campus aunque llegue `?campus=` (RNF-21).
- **Nombre completo y carrera solo con sesión:** la maqueta `Se-busca` muestra "Fernanda Ortiz · QFB" y `Se-busca-visitante` "Fernanda O.". Con sesión es lo mismo que ya da el perfil público (`GET /profiles/{id}`); la carrera respeta "Mostrar mi carrera" (HU-04). Se leen en lote: una consulta de usuarios y una de perfiles por página.
- **Sin las propias:** RN-06 no deja responder a una solicitud propia y las propias viven en "Mis solicitudes".
- **Presupuesto con mínimo y máximo inclusivos:** la app traduce las opciones de la maqueta ("Hasta $300" → `maxBudgetCents=30000`; "$300 – $1,000" → `30000` a `100000`; "Más de $1,000" → `minBudgetCents=100001`). Así la API no fija los rangos.
- **Fecha límite en días:** `neededWithinDays` cuenta desde hoy en el campus (UTC-6, como HU-13). Una solicitud sin fecha no tiene fecha límite y no sale con este filtro.
- **Búsqueda sin índice de palabras:** a diferencia de HU-09, las solicitudes no guardan `searchWords`. El tablero de un campus es chico (5 activas por persona, RN-12), la consulta ya va acotada por `ix_community_status_id` y así no hay script ni relleno. Si el tablero crece, se agrega `searchWords` como en `db/007-busqueda.js`.
- **Patrón sin `\b` ni `\p{L}`:** en PCRE dependen de UTF/UCP; se usan los mismos separadores que `SearchWord` y las vocales acentuadas (también en mayúscula) dentro de la clase de caracteres.
- **Errores propios del tablero** (`wanted.invalid_*`) y `listings.unknown_campus` reutilizado: el campus desconocido es el mismo error que en la vitrina.
- **Caché de salida del visitante:** política `wanted-board` de 30 s con su etiqueta, que se borra al publicar una solicitud y al eliminar una cuenta.

## Archivos
- **Nuevos:** `ContractTest/Fixtures/wanted-page.json` y `UnitTest/PurchaseRequests/RecordingOutputCacheStore.cs`.
- **Cambian:**
  - `Modules/PurchaseRequests/*` (servicio, controller, DTOs, errores, repositorios y el borrado de cuenta);
  - `Modules/Auth/MemberDirectory.cs` y los repositorios de usuarios (`FindManyAsync`);
  - `Modules/Profiles/ProfileDirectory.cs` (`FindCardsAsync`);
  - `Platform/Caching/CachingServiceCollectionExtensions.cs`.
- **Docs:** `docs/endpoints.md`, `docs/errores.md`, `docs/versionado.md` (`0.5.0-alpha.9`), `docs/trazabilidad.md` y `Memoria.md`.

## Validación
- Build Release 0/0, las cuatro suites y la batería compartida; `check_endpoints` y critic.
- `DatabaseTest` (MongoDB real) en el CI.
- Prueba en el celular con la app de HU-15 cuando se despliegue.
