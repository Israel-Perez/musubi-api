<!-- Sección compartida con musubi-app/musubi-api: si cambia, cámbiala en ambos repos y regístralo en las dos Memoria.md -->
# Módulos de MUSUBI (canónico)

Cada módulo es una épica en Jira (proyecto `SCRUM`) y organiza el código y las specs de los dos repos (RNF-23).
Las carpetas de código **se crean con la primera HU del módulo**; no se crean carpetas vacías por adelantado.

| ID | Módulo | Épica Jira | Carpeta de specs | Back (`musubi-api`) | Front (`musubi-app`) |
|---|---|---|---|---|---|
| M-01 | Autenticación y usuarios | SCRUM-5 | `specs/m01-auth/` | `Musubi.Api/Modules/Auth/` | `lib/features/auth/` |
| M-02 | Perfil | SCRUM-6 | `specs/m02-profile/` | `Musubi.Api/Modules/Profiles/` | `lib/features/profile/` |
| M-03 | Publicaciones | SCRUM-7 | `specs/m03-listings/` | `Musubi.Api/Modules/Listings/` | `lib/features/listings/` |
| M-04 | Marketplace | SCRUM-8 | `specs/m04-marketplace/` | `Musubi.Api/Modules/Marketplace/` | `lib/features/marketplace/` |
| M-05 | Solicitudes de compra | SCRUM-9 | `specs/m05-purchase-requests/` | `Musubi.Api/Modules/PurchaseRequests/` | `lib/features/purchase_requests/` |
| M-06 | Coincidencias | SCRUM-10 | `specs/m06-matching/` | `Musubi.Api/Modules/Matching/` | `lib/features/matching/` |
| M-07 | Chat | SCRUM-11 | `specs/m07-chat/` | `Musubi.Api/Modules/Chat/` | `lib/features/chat/` |
| M-08 | Pedidos, pagos y entregas | SCRUM-12 | `specs/m08-orders/` | `Musubi.Api/Modules/Orders/` | `lib/features/orders/` |
| M-09 | Notificaciones | SCRUM-13 | `specs/m09-notifications/` | `Musubi.Api/Modules/Notifications/` | `lib/features/notifications/` |
| M-10 | IA generativa | SCRUM-14 | `specs/m10-genai/` | `Musubi.Api/Modules/GenAi/` | `lib/features/genai/` |
| M-11 | Recomendaciones ML | SCRUM-15 | `specs/m11-recommendations/` | `Musubi.Api/Modules/Recommendations/` | `lib/features/recommendations/` |
| M-12 | Apoyo a vendedores | SCRUM-16 | `specs/m12-seller-support/` | `Musubi.Api/Modules/SellerSupport/` | `lib/features/seller_support/` |
| M-13 | Reputación | SCRUM-17 | `specs/m13-reputation/` | `Musubi.Api/Modules/Reputation/` | `lib/features/reputation/` |
| M-14 | Administración | SCRUM-18 | `specs/m14-admin/` | `Musubi.Api/Modules/Admin/` | `lib/features/admin/` |

Reglas:

- Un módulo no accede a los datos internos de otro: lo hace a través de su interfaz pública (servicio o evento).
- M-10 (IA) y M-11 (ML) son **opcionales en tiempo de ejecución**: si fallan, los demás módulos siguen funcionando (RNF-15).
- Las specs de cada HU van en `specs/mNN-<modulo>/HU-NN-<slug>/`, con el mismo ID y la misma carpeta en los dos repos.
<!-- Fin de sección compartida -->
