using Musubi.Api.Modules.Admin.Data;
using Musubi.Api.Modules.Auth.Data;

namespace Musubi.TestShared.Admin;

/// <summary>
/// HU-39/40/41 (RNF-04): comportamiento esperado de la bitacora de moderacion. Corre contra el fake en memoria
/// (UnitTest) y contra MongoDB real con db/ aplicado (DatabaseTest). Cada prueba usa sus propios ids.
/// </summary>
public abstract class ModerationLogRepositoryContract
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    internal abstract IModerationLogRepository Log { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ModerationLogDocument Entry(string subjectId, string action, int minute, string? reason = null) => new()
    {
        Id = AuthIds.New(),
        CommunityId = AuthIds.New(),
        ActorId = AuthIds.New(),
        Action = action,
        TargetType = "user",
        TargetId = subjectId,
        SubjectId = subjectId,
        Reason = reason,
        At = Now.AddMinutes(minute),
    };

    [Fact]
    public async Task FindBySubject_IsThatPersonOnly_NewestFirst_AndLimited()
    {
        var subject = AuthIds.New();
        var suspended = Entry(subject, ModerationActions.AccountSuspended, 0, "Pidió pagos fuera de Musubi.");
        var reactivated = Entry(subject, ModerationActions.AccountReactivated, 10);
        await Log.InsertAsync(suspended, Ct);
        await Log.InsertAsync(reactivated, Ct);
        await Log.InsertAsync(Entry(AuthIds.New(), ModerationActions.AccountSuspended, 5, "Otra persona."), Ct);

        var history = await Log.FindBySubjectAsync(subject, 10, Ct);
        var latest = await Log.FindBySubjectAsync(subject, 1, Ct);

        Assert.Equal([reactivated.Id, suspended.Id], history.Select(e => e.Id));
        Assert.Equal("Pidió pagos fuera de Musubi.", history[1].Reason);
        Assert.Equal(Now, history[1].At);
        Assert.Equal(reactivated.Id, Assert.Single(latest).Id);
        Assert.Empty(await Log.FindBySubjectAsync(AuthIds.New(), 10, Ct));
        Assert.Empty(await Log.FindBySubjectAsync("no-es-un-id", 10, Ct));
    }

    [Fact]
    public async Task CategoryChanges_AreStored_WithoutAPerson()
    {
        // HU-41: el validador de db/031 acepta cada accion de categorias (en Mongo, uno faltante falla al insertar).
        var category = AuthIds.New();
        string[] actions =
        [
            ModerationActions.CategoryCreated,
            ModerationActions.CategoryRenamed,
            ModerationActions.CategoryDeactivated,
            ModerationActions.CategoryReactivated,
        ];
        foreach (var action in actions)
        {
            await Log.InsertAsync(
                new ModerationLogDocument
                {
                    Id = AuthIds.New(),
                    CommunityId = AuthIds.New(),
                    ActorId = AuthIds.New(),
                    Action = action,
                    TargetType = ModerationTargets.Category,
                    TargetId = category,
                    Reason = action == ModerationActions.CategoryDeactivated ? null : "Ropa y batas",
                    At = Now,
                },
                Ct);
        }

        // Sin persona no entran al historial de ninguna cuenta.
        Assert.Empty(await Log.FindBySubjectAsync(category, 10, Ct));
    }
}
