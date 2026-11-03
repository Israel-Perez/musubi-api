# MCP del equipo (Model Context Protocol)

El documento de la materia permite usar MCP para acelerar el desarrollo (requisito 12). Aquí están los que usa el equipo, para qué sirven y cómo se configuran en **Claude Code** y en **OpenCode**. La guía general del flujo con agentes está en [`guia-equipo.md`](guia-equipo.md).

## Reglas

- **Ningún MCP guarda credenciales en el repo:** el inicio de sesión es por OAuth en el navegador, o con las credenciales locales de cada quien (`aws login`).
- **Lo que un agente escribe en Jira o Confluence lo ve todo el equipo:** antes de crear, transicionar o comentar, el agente lo dice en el chat.
- **Los MCP de solo lectura se prefieren para investigar.** Las acciones que cambian algo fuera del repo, como desplegar o borrar, siguen pasando por los workflows (`infra-up`, `release-play`), no por un MCP.

## MCP en uso

| MCP | Para qué lo usamos | Ejemplos de uso | Configuración |
|---|---|---|---|
| **Atlassian (Jira y Confluence)** | Trazabilidad: cada spec y PR lleva su clave `SCRUM-NN` | Crear la tarea de una spec técnica, comentar el avance con la evidencia (conteos de pruebas, run del despliegue), transicionar a "Finalizado" (`21` En curso, `31` En revisión, `41` Finalizado) | Servidor remoto `https://mcp.atlassian.com/v1/sse` con OAuth. Sitio `uttt-startup-musubi.atlassian.net` |
| **AWS (`aws-mcp`)** | Consultar documentación de AWS y el estado de la cuenta sin salir del agente | Revisar precios y capa gratuita antes de agregar un servicio (Rekognition, Comprehend), diagnosticar un despliegue | Servidor remoto de AWS (`https://aws-mcp.us-east-1.api.aws/mcp`) por el proxy `mcp-proxy-for-aws`, con las credenciales de `aws login` |
| **GitHub** (CLI `gh`, no MCP) | PR, checks, workflows y rulesets | `gh pr checks`, `gh workflow run infra-up.yml`, `gh api .../rulesets` | `gh auth login`. Se usa la CLI porque cubre todo y ya está en el flujo |

## Configuración

### Claude Code

```powershell
# Atlassian (OAuth en el navegador la primera vez)
claude mcp add --transport sse atlassian https://mcp.atlassian.com/v1/sse
# AWS: requiere uv (https://docs.astral.sh/uv/) y una sesion de `aws login`
claude mcp add aws-mcp -- uvx mcp-proxy-for-aws@latest https://aws-mcp.us-east-1.api.aws/mcp --metadata INSTALL_SOURCE=aws-cli
claude mcp list
```

También se pueden conectar desde claude.ai (Ajustes → Conectores), y Claude Code los toma de ahí.

### OpenCode

Se agregan en `~/.config/opencode/opencode.json` (configuración personal, fuera del repo):

```json
{
  "mcp": {
    "atlassian": { "type": "remote", "url": "https://mcp.atlassian.com/v1/sse" },
    "aws-mcp": {
      "type": "local",
      "command": ["uvx", "mcp-proxy-for-aws@latest", "https://aws-mcp.us-east-1.api.aws/mcp", "--metadata", "INSTALL_SOURCE=aws-cli"]
    }
  }
}
```

## Cómo se usan en el flujo SDD

1. **Spec:** el agente busca la HU en Jira (`SCRUM-NN`) y la concilia con la maqueta antes de escribir la spec.
2. **Implementación:** el MCP de AWS confirma límites y costos antes de proponer un servicio nuevo (regla de `00-01`).
3. **PR:** el agente comenta en la tarea de Jira con la evidencia (CI en verde, conteos y run del despliegue) y la mueve de estado.
