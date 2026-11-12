using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth.Data;

namespace Musubi.Api.Modules.Profiles.Data;

// Implementaciones en memoria para UseInMemoryDatabase=true (dotnet run sin MongoDB y suites WAF).
// TestShared/Profiles verifica que se comporten igual que las de Mongo.

/// <summary>Carreras en memoria, con la misma semilla que db/004-perfiles.js.</summary>
internal sealed class InMemoryCareerRepository : ICareerRepository
{
    /// <summary>Carreras de la UTTT, en su orden (propuesta a validar: HU-04 plan.md §4).</summary>
    public static readonly IReadOnlyList<(string Slug, string Name, string ShortName, string Level)> Seed =
    [
        ("tsu-energias-renovables-solar", "TSU en Energías Renovables, área Solar", "TSU Energías Renovables", CareerLevels.Tsu),
        ("tsu-quimica-industrial", "TSU en Química, área Industrial", "TSU Química Industrial", CareerLevels.Tsu),
        ("tsu-quimica-ambiental", "TSU en Química, área Tecnología Ambiental", "TSU Química Ambiental", CareerLevels.Tsu),
        ("tsu-nanotecnologia-materiales", "TSU en Nanotecnología, área Materiales", "TSU Nanotecnología", CareerLevels.Tsu),
        ("tsu-mantenimiento-industrial", "TSU en Mantenimiento Industrial, área Industrial", "TSU Mantenimiento Industrial", CareerLevels.Tsu),
        ("tsu-mantenimiento-maquinaria", "TSU en Mantenimiento Industrial, área Maquinaria Pesada", "TSU Maquinaria Pesada", CareerLevels.Tsu),
        ("tsu-mantenimiento-soldadura", "TSU en Mantenimiento, área Soldadura", "TSU Soldadura", CareerLevels.Tsu),
        ("tsu-contaduria", "TSU en Contaduría", "TSU Contaduría", CareerLevels.Tsu),
        ("tsu-administracion-capital-humano", "TSU en Administración, área Capital Humano", "TSU Capital Humano", CareerLevels.Tsu),
        ("tsu-administracion-proyectos", "TSU en Administración, área Formulación y Evaluación de Proyectos", "TSU Evaluación de Proyectos", CareerLevels.Tsu),
        ("tsu-procesos-manufactura", "TSU en Procesos Industriales, área Manufactura", "TSU Manufactura", CareerLevels.Tsu),
        ("tsu-plantas-hidrocarburos", "TSU en Construcción y Montaje de Plantas Industriales, área Hidrocarburos", "TSU Plantas Industriales", CareerLevels.Tsu),
        ("tsu-logistica-transporte", "TSU en Logística, área Transporte Terrestre", "TSU Logística Terrestre", CareerLevels.Tsu),
        ("tsu-logistica-suministros", "TSU en Logística, área Cadena de Suministros", "TSU Cadena de Suministros", CareerLevels.Tsu),
        ("tsu-negocios-ventas", "TSU en Desarrollo de Negocios, área Ventas", "TSU Ventas", CareerLevels.Tsu),
        ("tsu-negocios-mercadotecnia", "TSU en Desarrollo de Negocios, área Mercadotecnia", "TSU Mercadotecnia", CareerLevels.Tsu),
        ("tsu-mecatronica-instalaciones", "TSU en Mecatrónica, área Instalaciones Eléctricas Eficientes", "TSU Instalaciones Eléctricas", CareerLevels.Tsu),
        ("tsu-mecatronica-robotica", "TSU en Mecatrónica, área Robótica", "TSU Robótica", CareerLevels.Tsu),
        ("tsu-mecatronica-automatizacion", "TSU en Mecatrónica, área Automatización", "TSU Automatización", CareerLevels.Tsu),
        ("tsu-ti-software", "TSU en Tecnologías de la Información, área Desarrollo de Software Multiplataforma", "TSU Desarrollo de Software", CareerLevels.Tsu),
        ("tsu-ti-entornos-virtuales", "TSU en Tecnologías de la Información, área Entornos Virtuales y Negocios Digitales", "TSU Entornos Virtuales", CareerLevels.Tsu),
        ("tsu-ti-redes", "TSU en Tecnologías de la Información, área Infraestructura de Redes Digitales", "TSU Redes Digitales", CareerLevels.Tsu),
        ("ing-energias-renovables", "Ingeniería en Energías Renovables", "Ing. Energías Renovables", CareerLevels.Engineering),
        ("ing-quimica", "Ingeniería Química", "Ing. Química", CareerLevels.Engineering),
        ("ing-nanotecnologia", "Ingeniería en Nanotecnología", "Ing. Nanotecnología", CareerLevels.Engineering),
        ("ing-ambiental", "Ingeniería Ambiental", "Ing. Ambiental", CareerLevels.Engineering),
        ("ing-mantenimiento-industrial", "Ingeniería en Mantenimiento Industrial", "Ing. Mantenimiento Industrial", CareerLevels.Engineering),
        ("ing-procesos-industriales", "Ingeniería en Procesos y Operaciones Industriales", "Ing. Procesos Industriales", CareerLevels.Engineering),
        ("ing-plantas-industriales", "Ingeniería en Construcción y Montaje de Plantas Industriales", "Ing. Plantas Industriales", CareerLevels.Engineering),
        ("ing-mecatronica", "Ingeniería en Mecatrónica", "Ing. Mecatrónica", CareerLevels.Engineering),
        ("ing-software", "Ingeniería en Desarrollo y Gestión de Software", "Ing. Software", CareerLevels.Engineering),
        ("ing-entornos-virtuales", "Ingeniería en Entornos Virtuales y Negocios Digitales", "Ing. Entornos Virtuales", CareerLevels.Engineering),
        ("ing-redes-ciberseguridad", "Ingeniería en Redes Inteligentes y Ciberseguridad", "Ing. Redes y Ciberseguridad", CareerLevels.Engineering),
        ("lic-contaduria", "Licenciatura en Contaduría", "Lic. Contaduría", CareerLevels.Bachelor),
        ("lic-capital-humano", "Licenciatura en Gestión del Capital Humano", "Lic. Capital Humano", CareerLevels.Bachelor),
        ("lic-negocios-proyectos", "Licenciatura en Gestión de Negocios y Proyectos", "Lic. Negocios y Proyectos", CareerLevels.Bachelor),
        ("lic-redes-logisticas", "Licenciatura en Diseño y Gestión de Redes Logísticas", "Lic. Redes Logísticas", CareerLevels.Bachelor),
        ("lic-mercadotecnia", "Licenciatura en Innovación de Negocios y Mercadotecnia", "Lic. Mercadotecnia", CareerLevels.Bachelor),
    ];

    // UTTT: TSU del cuatrimestre 1 al 6; ingenieria y licenciatura del 7 al 11.
    private readonly CareerDocument[] careers = Seed
        .Select((c, i) => new CareerDocument
        {
            Id = (i + 1).ToString("x24", CultureInfo.InvariantCulture),
            CommunityId = InMemoryCommunityRepository.UtttId,
            Slug = c.Slug,
            Name = c.Name,
            ShortName = c.ShortName,
            Level = c.Level,
            FirstTerm = c.Level == CareerLevels.Tsu ? 1 : 7,
            LastTerm = c.Level == CareerLevels.Tsu ? 6 : 11,
            Order = i + 1,
        })
        .ToArray();

    /// <inheritdoc />
    public Task<IReadOnlyList<CareerDocument>> ListActiveAsync(string communityId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CareerDocument>>(
            careers.Where(c => c.Active && string.Equals(c.CommunityId, communityId, StringComparison.Ordinal)).OrderBy(c => c.Order).ToList());
}

/// <summary>Perfiles en memoria. Los documentos son inmutables: cada cambio reemplaza la entrada bajo el candado.</summary>
internal sealed class InMemoryProfileRepository : IProfileRepository
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, ProfileDocument> profiles = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<ProfileDocument?> FindAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(profiles.GetValueOrDefault(userId));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProfileDocument>> FindManyAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<ProfileDocument>>(userIds
                .Distinct(StringComparer.Ordinal)
                .Select(id => profiles.GetValueOrDefault(id))
                .OfType<ProfileDocument>()
                .ToList());
        }
    }

    /// <inheritdoc />
    public Task<ProfileDocument> SetDetailsAsync(ProfileOwner owner, string? careerSlug, int? term, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Upsert(owner, now, p => With(p, now, careerSlug, term, p.AvatarKey, p.Visibility)).After);

    /// <inheritdoc />
    public Task<ProfileDocument> SetVisibilityAsync(ProfileOwner owner, ProfileVisibility visibility, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Upsert(owner, now, p => With(p, now, p.CareerSlug, p.Term, p.AvatarKey, visibility)).After);

    /// <inheritdoc />
    public Task AddRatingAsync(ProfileOwner owner, int stars, bool onTime, DateTime now, CancellationToken cancellationToken)
    {
        Upsert(owner, now, p =>
        {
            var sum = p.Stats.RatingSum + stars;
            var count = p.Stats.RatingCount + 1;
            return new ProfileDocument
            {
                Id = p.Id,
                CommunityId = p.CommunityId,
                FullName = p.FullName,
                CareerSlug = p.CareerSlug,
                Term = p.Term,
                AvatarKey = p.AvatarKey,
                Visibility = p.Visibility,
                Stats = new ProfileStats
                {
                    RatingSum = sum,
                    RatingCount = count,
                    RatingAvg = sum / count,
                    OnTimeCount = p.Stats.OnTimeCount + (onTime ? 1 : 0),
                    Sales = p.Stats.Sales,
                    Purchases = p.Stats.Purchases,
                },
                SchemaVersion = p.SchemaVersion,
                UpdatedAt = now,
            };
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> SetAvatarAsync(ProfileOwner owner, string? avatarKey, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Upsert(owner, now, p => With(p, now, p.CareerSlug, p.Term, avatarKey, p.Visibility)).Before?.AvatarKey);

    /// <inheritdoc />
    public Task DeleteAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            profiles.Remove(userId);
        }

        return Task.CompletedTask;
    }

    private static ProfileDocument With(ProfileDocument p, DateTime now, string? careerSlug, int? term, string? avatarKey, ProfileVisibility visibility) => new()
    {
        Id = p.Id,
        CommunityId = p.CommunityId,
        FullName = p.FullName,
        CareerSlug = careerSlug,
        Term = term,
        AvatarKey = avatarKey,
        Visibility = visibility,
        Stats = p.Stats,
        SchemaVersion = p.SchemaVersion,
        UpdatedAt = now,
    };

    private (ProfileDocument? Before, ProfileDocument After) Upsert(ProfileOwner owner, DateTime now, Func<ProfileDocument, ProfileDocument> change)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (gate)
        {
            var before = profiles.GetValueOrDefault(owner.UserId);
            var current = before ?? new ProfileDocument
            {
                Id = owner.UserId,
                CommunityId = owner.CommunityId,
                FullName = owner.FullName,
                UpdatedAt = now,
            };
            var after = change(current);
            profiles[owner.UserId] = after;
            return (before, after);
        }
    }
}
