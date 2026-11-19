"""Compuerta del DAST (02-10, SCRUM-123): lee el reporte JSON de ZAP y decide.

Uso: python3 scripts/dast/zap_gate.py <zap-report.json> [resumen.md]

- FALLA (exit 1) si hay alguna alerta de riesgo Alto (riskcode 3).
- Las de riesgo Medio, Bajo e Informativo se listan en el resumen (GitHub Step Summary) para revisarlas.
Las excepciones revisadas van en .zap/rules.tsv (IGNORE con su motivo), nunca aqui.
"""

import json
import sys

RISK = {"3": "Alto", "2": "Medio", "1": "Bajo", "0": "Informativo"}


def main() -> int:
    report = json.load(open(sys.argv[1], encoding="utf-8"))
    alerts = [a for site in report.get("site", []) for a in site.get("alerts", [])]
    rows = sorted(alerts, key=lambda a: (-int(a.get("riskcode", "0")), a.get("name", "")))
    lines = [
        "## OWASP ZAP (DAST)",
        "",
        f"Alertas: {len(alerts)}. La compuerta falla con riesgo Alto.",
        "",
        "| Riesgo | Regla | Alerta | Instancias |",
        "|---|---|---|---|",
    ]
    for a in rows:
        lines.append(f"| {RISK.get(a.get('riskcode', '0'), '?')} | {a.get('pluginid')} | {a.get('name')} | {a.get('count', len(a.get('instances', [])))} |")
    summary = "\n".join(lines) + "\n"
    print(summary)
    if len(sys.argv) > 2:
        with open(sys.argv[2], "a", encoding="utf-8") as out:
            out.write(summary)
    high = [a for a in alerts if a.get("riskcode") == "3"]
    if high:
        print(f"dast: FAIL ({len(high)} alertas de riesgo Alto)")
        return 1
    print("dast: OK (sin alertas de riesgo Alto)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
