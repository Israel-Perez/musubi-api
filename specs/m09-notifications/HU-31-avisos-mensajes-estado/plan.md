# plan.md — HU-31 (API)

El diseño, las decisiones y los límites están en [`spec.md`](spec.md). Puntos clave:

- **FCM directo** (usuaria): sin el SDK de Firebase Admin; el JWT RS256 se firma con `System.Security.Cryptography` y las URLs de Google son fijas (sin SSRF por configuración).
- **Segundo plano:** la cola es en memoria por instancia y acotada (1000); quien escribe no espera a FCM.
- **Dos instancias:** los recordatorios corren en las dos; `ux_user_dedupe` deja pasar un solo aviso.
- **Excepciones al stack:** ninguna.
