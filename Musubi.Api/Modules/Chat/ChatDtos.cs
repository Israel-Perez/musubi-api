using System;
using System.Collections.Generic;

namespace Musubi.Api.Modules.Chat;

// Contratos HTTP y de tiempo real de M-07 (HU-20). Filas canonicas en docs/endpoints.md.

/// <summary>Iniciar una conversacion desde una publicacion (maqueta <c>Producto</c> → "Mensaje").</summary>
/// <param name="ProductId">Id de la publicacion.</param>
public sealed record StartConversationRequest(string ProductId);

/// <summary>Enviar un mensaje de texto.</summary>
/// <param name="Text">Texto (1 a 1000 caracteres; se recortan los espacios de los extremos).</param>
public sealed record SendMessageRequest(string Text);

/// <summary>Conversacion (maqueta <c>Chat-Valeria</c>: encabezado y tarjeta de la publicacion).</summary>
/// <param name="Id">Id.</param>
/// <param name="Role">Papel de quien consulta: <c>buyer</c> o <c>seller</c>.</param>
/// <param name="Counterpart">La otra persona.</param>
/// <param name="Context">Lo que origino la conversacion.</param>
/// <param name="CreatedAt">Creacion (UTC).</param>
public sealed record ConversationResponse(
    string Id,
    string Role,
    CounterpartResponse Counterpart,
    ConversationContextResponse Context,
    DateTime CreatedAt);

/// <summary>La otra persona de la conversacion (su foto y reputacion: <c>GET /profiles/{id}</c>).</summary>
/// <param name="Id">Id del usuario.</param>
/// <param name="Name">Nombre completo.</param>
public sealed record CounterpartResponse(string Id, string Name);

/// <summary>
/// Tarjeta del elemento de contexto. Precio, estado y foto son los de ahora; si la publicacion ya no se
/// puede ver, <c>available</c> es <c>false</c> y solo queda el nombre guardado.
/// </summary>
/// <param name="Type"><c>product</c>.</param>
/// <param name="Id">Id del elemento.</param>
/// <param name="Title">Nombre.</param>
/// <param name="PriceCents">Precio en centavos, o <c>null</c> si no esta disponible.</param>
/// <param name="Condition"><c>new</c>/<c>used</c>, o <c>null</c>.</param>
/// <param name="ConditionDetail">Detalle de un usado, o <c>null</c>.</param>
/// <param name="Photo">Ruta de la portada, o <c>null</c>.</param>
/// <param name="Available">Se puede abrir su detalle.</param>
public sealed record ConversationContextResponse(
    string Type,
    string Id,
    string Title,
    long? PriceCents,
    string? Condition,
    string? ConditionDetail,
    string? Photo,
    bool Available);

/// <summary>Mensaje de texto. Tambien es la carga del evento <c>messageCreated</c> del hub.</summary>
/// <param name="Id">Id (ordenado por tiempo).</param>
/// <param name="ConversationId">Conversacion.</param>
/// <param name="SenderId">Quien lo envio (la app lo compara con su propio id).</param>
/// <param name="Text">Texto.</param>
/// <param name="SentAt">Envio (UTC).</param>
public sealed record MessageResponse(string Id, string ConversationId, string SenderId, string Text, DateTime SentAt);

/// <summary>Pagina de mensajes, del mas nuevo al mas viejo.</summary>
/// <param name="Items">Mensajes.</param>
/// <param name="NextCursor">Cursor para los anteriores, o <c>null</c> si no hay mas.</param>
public sealed record MessagePage(IReadOnlyList<MessageResponse> Items, string? NextCursor);
