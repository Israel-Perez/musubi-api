using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.GenAi.Data;

namespace Musubi.TestShared.GenAi;

/// <summary>
/// HU-42: el registro del uso de la IA (resumenes y acuerdos confirmados, con o sin correcciones). Corre contra el fake
/// en memoria (UnitTest) y contra MongoDB real con db/ aplicado (DatabaseTest).
/// </summary>
public abstract class AiUsageRepositoryContract
{
    private static readonly DateTime Now = DateTime.UtcNow.Date.AddHours(18);

    internal abstract IAiUsageRepository Usage { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AiUsageDocument Use(string campus, string kind, bool corrected = false, int minutes = 0) => new()
    {
        Id = AuthIds.New(),
        CommunityId = campus,
        Kind = kind,
        Corrected = corrected,
        At = Now.AddMinutes(minutes),
        ExpiresAt = Now.AddDays(365),
    };

    [Fact]
    public async Task Counts_TheSummaries_TheConfirmed_AndTheUncorrected_OfTheCampusInThePeriod()
    {
        var campus = AuthIds.New();
        await Usage.RecordAsync(Use(campus, AiUsageKinds.Summary), Ct);
        await Usage.RecordAsync(Use(campus, AiUsageKinds.Summary, minutes: 1), Ct);
        await Usage.RecordAsync(Use(campus, AiUsageKinds.Confirmed, corrected: false, minutes: 2), Ct);
        await Usage.RecordAsync(Use(campus, AiUsageKinds.Confirmed, corrected: true, minutes: 3), Ct);
        await Usage.RecordAsync(Use(AuthIds.New(), AiUsageKinds.Summary), Ct);

        Assert.Equal(new AiUsage(2, 2, 1), await Usage.UsageAsync(campus, Now.AddMinutes(-1), Now.AddMinutes(10), Ct));
        Assert.Equal(new AiUsage(1, 0, 0), await Usage.UsageAsync(campus, Now.AddMinutes(1), Now.AddMinutes(2), Ct));
        Assert.Equal(new AiUsage(0, 0, 0), await Usage.UsageAsync("no-es-un-id", Now, Now.AddHours(1), Ct));
    }
}
