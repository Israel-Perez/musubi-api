# plan.md — 02-08 CI/CD

## 1. Componentes
- **Workflows:** `ci-pr.yml`, `infra-up.yml` e `infra-down.yml`, ya existentes; esta spec los documenta.
- **Ruleset "Main Proteccion"** en `main`, creado con la API de GitHub.
- **Dependabot:** `.github/dependabot.yml`.

## 2. Decisiones
- **CD manual y solo desde `main`:** desplegar cuesta dinero y cambia lo que ven los testers; nadie despliega desde una rama.
- **OIDC en lugar de llaves de AWS en GitHub:** el token dura minutos y el rol solo puede desplegar este proyecto.
- **El ruleset convive con la protección clásica:** la clásica ya exigía los checks. El ruleset agrega lo que pide la materia (nombre, borrado y Copilot).
- **Bypass del administrador solo por PR:** hoy no hay un revisor fijo. Sin el bypass, ningún cambio podría fusionarse. Con él, el CI sigue siendo obligatorio.

## 3. Validación
`actionlint`, las autopruebas de las guardas y una corrida de `infra-up` con la prueba de humo en 200.
