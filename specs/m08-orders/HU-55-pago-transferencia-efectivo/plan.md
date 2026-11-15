# plan.md — HU-55 Pagar un pedido por transferencia o en efectivo

## 1. Componentes
- `Modules/Orders/`:
  - `Payouts.cs`, `PayoutService.cs`, `PayoutsController.cs` y `OrderPaymentService.cs`;
  - cambios en `OrderLifecycleService`, `OrderService`, `OrdersController`, `OrdersDtos` y `OrdersErrors`;
  - `Data/PayoutAccountRepositories.cs`, `Data/OrderDocuments.cs` y `Data/OrderRepositories.cs`.
- `Modules/Notifications/NotificationKinds.cs`: los tres avisos de la transferencia.
- `db/027-pagos.js` y `MongoSchema.ExpectedVersion = 27`; API `0.5.0-alpha.35`.

## 2. Decisiones (con su porqué)
- **Plazo (usuaria):** recordatorio a las 24 h, sin cancelar. Cancelar solo después de que ya pagaron dejaría el dinero con quien vende y el pedido cerrado.
- **Solo CLABE (usuaria):** menos datos sensibles guardados (R10). La tarjeta de la maqueta muestra la CLABE.
- **Sin comprobante (usuaria):** las fotos se sirven públicas por el CDN; queda `NOTE (HU-55)`.
- **AES-256-GCM con el id como dato asociado:** una CLABE copiada a otro documento no se descifra. El nonce es nuevo en cada guardado. No se usa Data Protection de ASP.NET porque necesitaría compartir el anillo de llaves entre las 2 instancias.
- **Llave temporal solo en Development:** las pruebas y el desarrollo funcionan sin secretos. En Production, sin llave, se apaga la transferencia en vez de tumbar la API (RNF-15).
- **"Sí, me llegó el pago" = aceptar:** un solo paso para quien vende; el pedido nace Pendiente y la validación lo confirma.
- **El código de entrega se guarda en claro y solo lo ve quien compra:** quien compra tiene que verlo para dictarlo. Contra la fuerza bruta, 5 intentos atómicos (se cuentan antes de comparar) y comparación en tiempo constante.
- **El titular:** todas sus palabras deben estar en el nombre verificado, para aceptar "Ana Ramírez" de "Ana Sofía Ramírez López".
- **Bancos:** se toman de la lista de participantes de SPEI de Banxico (consultada el 15-Nov-2026). La clave de la CLABE son los últimos 3 dígitos de la clave de la institución.

## 3. Excepciones al stack
Ninguna: `System.Security.Cryptography` de .NET.

## 4. Guardarraíles
- La parte del pedido va en el filtro de cada escritura (OWASP API1).
- La CLABE nunca va en una respuesta de `/payouts/*` ni en los logs.
- El rate limit global y los tamaños de cuerpo de M-08.

## 5. Validación
- Unit, Integration, Security, Contract y DatabaseTest (ver `spec.md` → Tests).
- Prueba en el celular con la llave en Parameter Store (T6).

## 6. Secuencia
T1 → T2 → T3 → T4 → T5 (app) → T6.
