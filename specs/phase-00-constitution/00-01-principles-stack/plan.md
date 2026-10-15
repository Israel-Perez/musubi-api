# plan.md — 00-01 Principios y stack

1. **Mapeo de componentes e impacto**
   - Crear: `.specify/memory/constitution.md` (§1–§5 compartidas, §6–§11 propias) y esta spec.
   - Modificar: `AGENTS.md` §1 (cita a 00-01).
2. **Decisiones de arquitectura**
   - Monolito modular en un proyecto (`Musubi.Api`) en lugar de N proyectos por capa: el equipo es de 4 personas y el MVP dura 10 semanas; los módulos se separan por carpeta e interfaz (RNF-23) y se pueden extraer después.
   - PostgreSQL + EF Core (Npgsql) por ser gratuito, estar administrado en AWS y tener buen soporte de búsqueda de texto (HU-09).
3. **Guardarraíles DevSecOps**
   - Ninguna librería entra sin evaluar su licencia, mantenimiento y seguridad (regla de excepción).
   - Sin secretos en el código: user-secrets o archivos ignorados.
4. **Validación**
   - Revisión humana (1 revisor) y build Release 0/0 con el stack declarado.
5. **Secuencia**
   1. Redactar la constitución y esta spec. 2. Citarla desde AGENTS.md y 00-02. 3. Verificar que cada principio tenga su guardarraíl.
