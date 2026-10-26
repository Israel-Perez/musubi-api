# plan.md — HU-47 Aviso de privacidad (back)

1. **Componentes:** `LegalOptions` y la validación de los consentimientos en `RegistrationService`; las claves `Legal:*` en los appsettings.
2. **Decisiones:** la versión acordada (`2026-10`) se guarda en el usuario, no el texto. El texto vive en un solo lugar (`musubi-app/docs/legal/`) y se versiona con Git.
3. **Guardarraíles:** sin aceptación no hay cuenta (prueba de integración).
4. **Validación:** IntegrationTest.
5. **Secuencia:** opciones → validación → pruebas → claves en 01-04.
