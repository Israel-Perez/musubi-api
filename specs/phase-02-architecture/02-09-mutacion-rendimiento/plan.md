# plan.md — 02-09 Mutación y rendimiento

## Decisiones
- **xUnit v2 solo en `MutationTest`:** el resto de las suites sigue en xUnit v3 + MTP. Migrar todo para Stryker cuesta más de lo que aporta.
- **Rendimiento en proceso para el CI:** sin red ni base, la medición es estable y el umbral detecta regresiones del código (por ejemplo, quitar la caché).
- **Mutación fuera del CI de PR:** tarda varios minutos y no debe frenar cada cambio. Corre cada semana.
