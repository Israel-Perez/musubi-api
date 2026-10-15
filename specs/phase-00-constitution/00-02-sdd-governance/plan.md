# plan.md — 00-02 Gobernanza SDD

1. **Componentes:** `specs/_template.md` (compartida), esta spec, `AGENTS.md` §2, `Memoria.md`, `docs/trazabilidad.md`, plantilla de PR.
2. **Decisiones:**
   - Tres archivos por spec (`spec`/`plan`/`task`) como en la referencia: separan el qué, el cómo y la evidencia.
   - Estado "Borrador con evidencia" para no simular firmas: el equipo aún no designa revisores.
   - IDs comunes a los dos repos (`HU-NN` con la misma carpeta) para que una HU se rastree de punta a punta.
3. **Guardarraíles:** criterios medibles; ninguna modificación de la constitución sin registro; secciones compartidas verificadas con `scripts/check_shared_sections.ps1`.
4. **Validación:** comparación estructural de las 9 specs piloto (fases 00 y 01).
5. **Secuencia:** plantilla → specs 00 → specs 01 → AGENTS/Memoria → verificación estructural.
