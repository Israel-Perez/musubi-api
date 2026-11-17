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

    /// <summary>Se publico algo que coincide con mi solicitud (HU-29).</summary>
    public const string ProductMatch = "product_match";

    /// <summary>Se publico lo que busque sin resultados ("Avísame cuando aparezca", HU-12).</summary>
    public const string SavedSearchMatch = "saved_search_match";

    /// <summary>Mi publicacion lleva 3 semanas sin venderse (HU-36).</summary>
    public const string SlowListing = "slow_listing";

    /// <summary>HU-55: "Ana dice que ya te transfirió" (a quien vende).</summary>
    public const string TransferReported = "transfer_reported";

    /// <summary>HU-55: quien vende todavia no ve la transferencia (a quien compra).</summary>
    public const string TransferNotReceived = "transfer_not_received";

    /// <summary>HU-55: a las 24 h quien vende aun no confirma la transferencia (decision de la usuaria del 15-Nov-2026).</summary>
    public const string TransferReminder = "transfer_reminder";

    /// <summary>HU-52: "Luis apartó tu Juego de escuadras" (a quien vende).</summary>
    public const string ReservationCreated = "reservation_created";

    /// <summary>HU-52: la otra parte cancelo el apartado.</summary>
    public const string ReservationCancelled = "reservation_cancelled";

    /// <summary>HU-52: pasaron 24 h sin recogerlo (a los dos).</summary>
    public const string ReservationExpired = "reservation_expired";

    /// <summary>HU-51: la agotada de la que pedi "Avísame" volvio a tener piezas.</summary>
    public const string BackInStock = "back_in_stock";

    /// <summary>"Revisamos tu reporte" a quien reporto (HU-39; solo push: no tiene pestana en el historial).</summary>
    public const string ReportReviewed = "report_reviewed";

    /// <summary>"Retiramos tu publicacion" a quien vende (HU-39; solo push).</summary>
    public const string ProductRemoved = "product_removed";
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

    /// <summary>Una publicacion (HU-29: lo que coincide con mi solicitud).</summary>
    public const string Product = "product";

    /// <summary>"Mis productos", con "Llevan tiempo sin venderse" (HU-36; el id es la publicacion).</summary>
    public const string MyProducts = "my_products";

    /// <summary>HU-52: un apartado ("Mis apartados").</summary>
    public const string Reservation = "reservation";
}
