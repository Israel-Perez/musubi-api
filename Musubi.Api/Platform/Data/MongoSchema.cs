namespace Musubi.Api.Platform.Data;

/// <summary>Version del esquema y nombres de colecciones (catalogo canonico: docs/modelo-datos.md).</summary>
internal static class MongoSchema
{
    /// <summary>Version que esta API necesita; debe ser igual al numero del ultimo script de db/ (SchemaVersionTests).</summary>
    public const int ExpectedVersion = 18;

    /// <summary>Coleccion de metadatos (documento "schema").</summary>
    public const string Meta = "_meta";

    /// <summary>Campus / universidades (RNF-21).</summary>
    public const string Communities = "communities";

    /// <summary>Identidad y credenciales.</summary>
    public const string Users = "users";

    /// <summary>Codigos de verificacion (TTL).</summary>
    public const string VerificationCodes = "verificationCodes";

    /// <summary>Refresh tokens (solo hash; TTL).</summary>
    public const string RefreshTokens = "refreshTokens";

    /// <summary>Categorias por comunidad (HU-06; las administra HU-41).</summary>
    public const string Categories = "categories";

    /// <summary>Publicaciones de productos (HU-06, HU-50).</summary>
    public const string Products = "products";

    /// <summary>Perfil de cada persona (HU-04; <c>_id</c> = id del usuario).</summary>
    public const string Profiles = "profiles";

    /// <summary>Carreras por comunidad (HU-04; las administra HU-41).</summary>
    public const string Careers = "careers";

    /// <summary>Conversaciones de compra y venta (HU-20; bandeja HU-22).</summary>
    public const string Conversations = "conversations";

    /// <summary>Mensajes de las conversaciones (HU-20).</summary>
    public const string Messages = "messages";

    /// <summary>Reportes de personas, publicaciones o problemas (HU-28; los revisa HU-39).</summary>
    public const string Reports = "reports";

    /// <summary>Bloqueos entre personas (HU-57).</summary>
    public const string Blocks = "blocks";

    /// <summary>Solicitudes de compra "Se busca" (HU-13; ofertas HU-16).</summary>
    public const string WantedRequests = "wantedRequests";

    /// <summary>Ofertas "Yo tengo este producto" (HU-16).</summary>
    public const string Offers = "offers";

    /// <summary>Productos guardados en favoritos (HU-53).</summary>
    public const string Favorites = "favorites";

    /// <summary>Pedidos (HU-23).</summary>
    public const string Orders = "orders";

    /// <summary>Puntos de entrega del campus (HU-23).</summary>
    public const string MeetingPoints = "meetingPoints";

    /// <summary>Consecutivos, como el numero de los pedidos (HU-23).</summary>
    public const string Counters = "counters";

    /// <summary>Acuerdos con IA (HU-32/33/34, db/015).</summary>
    public const string Agreements = "agreements";

    /// <summary>Avisos del centro de notificaciones (HU-31, db/017).</summary>
    public const string Notifications = "notifications";

    /// <summary>Tokens de FCM de los dispositivos (HU-31, db/017).</summary>
    public const string DeviceTokens = "deviceTokens";

    /// <summary>Preferencias de apariencia por cuenta (HU-54, db/018).</summary>
    public const string Preferences = "preferences";
}
