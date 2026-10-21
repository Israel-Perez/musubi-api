# plan.md — 00-01 Principios y stack

1. **Mapeo de componentes e impacto**
   - Crear: `.specify/memory/constitution.md` (§1–§5 compartidas, §6–§11 propias) y esta spec.
   - Modificar: `AGENTS.md` §1 (cita a 00-01).
2. **Decisiones de arquitectura**
   - Monolito modular en un proyecto (`Musubi.Api`) en lugar de N proyectos por capa: el equipo es de 4 personas y el MVP dura 10 semanas; los módulos se separan por carpeta e interfaz (RNF-23) y se pueden extraer después.
   - ~~PostgreSQL + EF Core (Npgsql)~~ → **MongoDB** (20-Oct-2026, SCRUM-66): el lineamiento 14 de la materia exige una base NoSQL. Atlas M0 es gratis (cumple "costo cero", lineamiento 15) y el servicio externo está permitido (lineamiento 13). La búsqueda por palabra clave (HU-09) usa un índice de texto de MongoDB (02-01). Alternativa descartada: DynamoDB (las consultas con filtros combinados de HU-10 son más costosas de modelar y no hay un script portable para la entrega).
3. **Guardarraíles DevSecOps**
   - Ninguna librería entra sin evaluar su licencia, mantenimiento y seguridad (regla de excepción).
   - Sin secretos en el código: user-secrets o archivos ignorados.
4. **Validación**
   - Revisión humana (1 revisor) y build Release 0/0 con el stack declarado.
5. **Secuencia**
   1. Redactar la constitución y esta spec. 2. Citarla desde AGENTS.md y 00-02. 3. Verificar que cada principio tenga su guardarraíl.
