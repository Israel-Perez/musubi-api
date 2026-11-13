namespace Musubi.Api.Modules.Notifications;

// Tipos, pestanas y destinos de los avisos (HU-31): parte del contrato publico del modulo (INotificationCenter).

/// <summary>Pestanas de la maqueta.</summary>
internal static class NotificationCategories
{
    /// <summary>"Pedidos".</summary>
    public const string Orders = "orders";

    /// <summary>"Solicitudes".</summary>
    public const string Requests = "requests";

    /// <summary>"Coincidencias" (HU-19 y HU-29).</summary>
    public const string Matches = "matches";

    /// <summary>Las tres.</summary>
    public static readonly string[] All = [Orders, Requests, Matches];
}

/// <summary>Tipos de aviso.</summary>
internal static class NotificationTypes
{
    /// <summary>Mensaje nuevo (solo push: el historial es la bandeja, HU-22).</summary>
    public const string Message = "message";

    /// <summary>Pedido nuevo, a quien vende (HU-23).</summary>
    public const string OrderCreated = "order_created";

    /// <summary>Quien vende acepto mi pedido (HU-24).</summary>
    public const string OrderConfirmed = "order_confirmed";

    /// <summary>La otra parte rechazo o cancelo el pedido (HU-24).</summary>
    public const string OrderCancelled = "order_cancelled";

    /// <summary>La otra parte confirmo su lado de la entrega (HU-24).</summary>
    public const string OrderHandover = "order_handover";

    /// <summary>El pedido se completo (HU-24).</summary>
    public const string OrderCompleted = "order_completed";

    /// <summary>La otra persona confirmo el acuerdo con IA (HU-34).</summary>
    public const string AgreementConfirmed = "agreement_confirmed";

    /// <summary>Oferta nueva a mi solicitud (HU-16).</summary>
    public const string OfferReceived = "offer_received";

    /// <summary>Aceptaron mi oferta (HU-17).</summary>
    public const string OfferAccepted = "offer_accepted";

    /// <summary>No eligieron mi oferta (HU-17).</summary>
    public const string OfferRejected = "offer_rejected";

    /// <summary>Mi solicitud vence en menos de 24 horas.</summary>
    public const string RequestExpiring = "request_expiring";

    /// <summary>Mi solicitud vencio.</summary>
    public const string RequestExpired = "request_expired";

    /// <summary>Una solicitud nueva es compatible con mi publicacion (HU-19).</summary>
    public const string WantedMatch = "wanted_match";

    /// <summary>La solicitud a la que ofreci se cerro o se cancelo (HU-30).</summary>
    public const string OfferClosed = "offer_closed";
}

/// <summary>Lo que abre un aviso.</summary>
internal static class NotificationTargets
{
    /// <summary>Un chat.</summary>
    public const string Conversation = "conversation";

    /// <summary>Un pedido.</summary>
    public const string Order = "order";

    /// <summary>Mi solicitud "Se busca", con sus ofertas (HU-30).</summary>
    public const string Wanted = "wanted";

    /// <summary>Una solicitud del tablero, para responder "Yo tengo este producto" (HU-19).</summary>
    public const string WantedBoard = "wanted_board";

    /// <summary>Nada que abrir (la solicitud ya no esta activa, HU-30).</summary>
    public const string None = "none";
}
