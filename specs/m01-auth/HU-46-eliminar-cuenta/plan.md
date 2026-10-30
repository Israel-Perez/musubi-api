# plan.md — HU-46 Eliminar mi cuenta y mis datos

## 1. Componentes
- `Modules/Auth/AccountController.cs` y `AccountDeletionService.cs` (con la interfaz `IAccountDataEraser`).
- `Modules/Listings/ListingAccountEraser.cs`.
- Métodos nuevos de repositorio:
  - `DeleteAllForUserAsync` en códigos y sesiones;
  - `DeleteBySellerAsync` en productos.
- `IMediaStorage.DeletePrefixAsync`.
- `infra/stack/iam.tf`: `s3:ListBucket` en `media/products/*`.

## 2. Decisiones
- **Pedir la contraseña actual** (agregado sobre la maqueta): borrar la cuenta es irreversible. Una sesión abierta en un teléfono perdido no debe bastar.
- **`POST /account/delete` y no `DELETE /account` con cuerpo:** algunos proxies y clientes descartan el cuerpo de un `DELETE`, y la contraseña viaja en el cuerpo, nunca en la URL.
- **`auth.wrong_password` responde 400:** la app trata un 401 como sesión vencida y la renovaría.
- **Un borrador por módulo (`IAccountDataEraser`):** M-01 no conoce las colecciones de los demás. Cada HU que guarde datos personales agrega el suyo, sin tocar Auth.
- **La cuenta se borra al final:** sin transacciones (R7), es el orden que permite reintentar si algo falla a medias.
- **Fotos por prefijo:** también caen las que se subieron y no se publicaron. No se depende de que la publicación exista.

## 3. Guardarraíles
- Rate limit `auth` (por IP): impide adivinar la contraseña con este endpoint.
- Mismo costo de verificación cuando la cuenta ya no existe.
- El log solo lleva el id interno.

## 4. Validación
- Las pruebas de `spec.md` y `check_endpoints`.
- `terraform validate` del permiso nuevo.
- Despliegue con `infra-up` y prueba en el celular: borrar una cuenta de prueba con una publicación y verificar que la foto da 404 en CloudFront.

## 5. Secuencia
1. Repositorios y almacenamiento.
2. Servicio y endpoint.
3. Borrador de Listings.
4. Pruebas.
5. IAM.
6. Documentación.
7. Despliegue.
