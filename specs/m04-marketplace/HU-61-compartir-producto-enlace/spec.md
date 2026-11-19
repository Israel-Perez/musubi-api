# HU-61 — Compartir un producto con un enlace

**HU / Jira / Sprint / Módulo / Contraparte:** HU-61 / [SCRUM-101](https://uttt-startup-musubi.atlassian.net/browse/SCRUM-101) / Por asignar (posterior al MVP) / M-04 / [musubi-app:HU-61](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m04-marketplace/HU-61-compartir-producto-enlace)

## Contexto
Como usuario, quiero compartir una publicación mediante un enlace para que otras personas puedan abrirla en Musubi. Jira propone `https://musubiapp.com.mx/p/{id}`, Android App Links y una landing; queda pendiente la política pública de metadatos. Decisión de la usuaria: la vista previa web será genérica, sin nombre, precio ni foto. Esto no cambia el acceso al detalle dentro de la app: HU-48 permite explorar publicaciones como visitante y, con sesión, el API restringe por campus. **Depende de**: `02-01`, `02-03`, `HU-11` y `HU-48`.

## Requisitos
1. Reutilizar el detalle del producto para los enlaces compartidos, sin crear un endpoint ni DTO duplicado.
2. Al abrir el enlace con la app instalada, dirigir al detalle existente y respetar sus reglas: visitante conforme a HU-48; con sesión, publicación permitida para el campus del token.
3. Sin app instalada, mostrar una página genérica de Musubi que no publique nombre, precio, fotografía, identificador del vendedor ni otros datos del producto.
4. No generar metadatos Open Graph ni contenido indexable de producto en el fallback web.
5. Para producto pausado, vendido, quitado, inexistente, de otro campus o bloqueado, el flujo autenticado mostrará no disponible sin revelar la causa; conservar la presentación anónima de publicaciones públicas/agotadas que define HU-48.
6. No tratar el identificador del enlace como credencial ni registrar destinatarios o historial de enlaces compartidos.

Aplican RNF-03 (autorización), RNF-05/RNF-07 (privacidad), RNF-21 (campus), RNF-23 (límites modulares), RNF-25 (pruebas automatizadas) y OWASP API1/API3.

## Diseño
- M-04 reutiliza `Musubi.Api/Modules/Listings/`; no crear `Modules/Marketplace/`.
- Reutilizar `GET /api/v1/products/{id}`. El contrato conserva el acceso anónimo de HU-48 y restringe por campus cuando hay sesión; no se crea excepción por conocer el enlace.
- No agregar persistencia, índices, configuración ni adaptador nuevo al API.
- La landing, sus metadatos y `assetlinks.json` corresponden a la app/hosting. Esta spec documenta el límite API y enlaza su contraparte Flutter.

## Contratos
- Reutilizar la fila [`GET /api/v1/products/{id}`](../../../docs/endpoints.md): sesión opcional por HU-48, con filtro por campus si hay token, y 404 para detalle no visible.
- Contrato externo del enlace: `https://musubiapp.com.mx/p/{id}`; sin token ni datos privados en la URL.
- No se agrega fila a `docs/endpoints.md`, DTO, error, fixture ni colección.

## Tests
- `IntegrationTest/Listings/ListingFlowTests.cs`: detalle anónimo y autenticado, visibilidad y 404.
- `SecurityTest/Auth/ListingsAuthTests.cs`: campus y autorización del detalle.
- `ContractTest/ListingsContractTests.cs`: contrato de `ProductDetailResponse`.
- `DatabaseTest/MongoListingRepositoryTests.cs`: no requiere cambio; el endpoint utiliza el repositorio actual.
- En la contraparte `musubi-app:HU-61`: pruebas de share sheet, routing/deep link, App Links y fallback genérico. Landing/HTML/OG deben probarse en el repositorio que administra el sitio.

## Criterios
- [x] El API no agrega endpoint ni cambia la forma de `ProductDetailResponse` → `pwsh scripts/check_endpoints.ps1` → `OK` y `ContractTest/ListingsContractTests.cs`.
- [x] Con sesión, el detalle del enlace mantiene la restricción de campus y responde no disponible para contenido no visible → `IntegrationTest/Listings/ListingFlowTests.cs` y `SecurityTest/Auth/ListingsAuthTests.cs`.
- [ ] Sin sesión, el detalle conserva la conducta pública de HU-48; la landing no devuelve metadatos dinámicos del producto → pruebas de HU-48 y prueba HTML/OG de la contraparte.
- [x] Build Release 0 errores/0 advertencias; Unit 974, Integration 216, Security 333 y Contract 31 verdes; critic PASS.

## Límites
- La vista previa web con metadatos de producto queda descartada por decisión de privacidad; cambiarla requiere nueva decisión y revisión de RNF-05/RNF-07.
- La API conserva el comportamiento anónimo actual de HU-48. Si Jira exige que solo cuentas del mismo campus puedan ver el producto también dentro de la app, el equipo debe revisar HU-48 y modificar explícitamente ese contrato; no se resuelve solo en el flujo de enlace.
- La landing, el despliegue de `assetlinks.json` y la verificación en tienda no pertenecen a este repo.
- Jira marca HU-61 posterior al MVP y sin sprint asignado.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 revisor distinto del autor)
- **Fecha:** 19-Nov-2026
- **Detalle:** Se verificó el endpoint existente en build Release 0/0 y suites Unit/Integration/Security/Contract (974/216/333/31); sin cambios de código API ni de contrato. El 19-Nov-2026 `/.well-known/assetlinks.json` respondió 404; landing/OG y publicación del archivo siguen pendientes del administrador del dominio.
