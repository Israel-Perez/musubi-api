# plan.md — HU-64 Ver insignias de confianza de quien vende

## 1. Componentes
- `Modules/Profiles/`: `Data/ProfileDocuments.cs` (`Responses`, `ResponseSample`), `IProfileRepository.AddResponseAsync` (Mongo y memoria), `ProfileDirectory.cs` (`IProfileResponses`, `ProfileResponses`, `SellerBadges`), `ProfilesDtos.cs` y `ProfileService.PublicAsync`.
- `Modules/Chat/ChatService.cs`: registra la respuesta.

## 2. Decisiones (con su porqué)
- **Umbrales (usuaria):** 4.5 ★ con 5 ventas; la mediana con 3 respuestas en 30 días.
- **Se guarda al contestar y se calcula al leer:** no hace falta un proceso periódico ni leer los mensajes; la ventana de 30 días se aplica al leer.
- **`$push` con `$slice: -20` en una sola escritura:** dos respuestas a la vez no rompen el límite.
- **La mediana y no el promedio:** una respuesta de días no arruina la insignia.
- **Sin script:** `db/004` no tiene `additionalProperties: false`; `responses` entra sin cambiar el esquema.

## 3. Excepciones al stack
Ninguna.

## 4. Guardarraíles
- Las respuestas son privadas: solo sale la mediana.
- El perfil público sigue con su visibilidad (mismo campus, sin bloqueos; HU-57).

## 5. Validación
Unit, Integration, Security, Contract y DatabaseTest (la batería del perfil contra MongoDB real).

## 6. Secuencia
Documento y repositorio → reglas → chat → perfil público → documentación.
