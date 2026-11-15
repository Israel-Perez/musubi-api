using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Orders.Data;

namespace Musubi.TestShared.Orders;

/// <summary>
/// HU-55: comportamiento esperado del repositorio de cuentas para recibir pagos. Corre contra el fake en memoria
/// (UnitTest) y contra MongoDB real con db/ aplicado (DatabaseTest).
/// </summary>
public abstract class PayoutAccountRepositoryContract
{
    private static readonly DateTime Now = DateTime.UtcNow.Date.AddHours(18);

    internal abstract IPayoutAccountRepository Accounts { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PayoutAccountDocument Account(string userId, string last4, DateTime at) => new()
    {
        Id = userId,
        CommunityId = AuthIds.New(),
        HolderName = "Ana Sofía Ramírez",
        BankCode = "012",
        BankName = "BBVA México",
        ClabeCiphertext = "v1:" + Convert.ToBase64String(new byte[46]),
        ClabeLast4 = last4,
        VerifiedAt = at,
        CreatedAt = at,
        UpdatedAt = at,
    };

    [Fact]
    public async Task Save_CreatesOne_AndAgainReplacesIt_KeepingWhenItWasCreated()
    {
        var ana = AuthIds.New();

        var first = await Accounts.SaveAsync(Account(ana, "4821", Now), Ct);
        var second = await Accounts.SaveAsync(Account(ana, "9134", Now.AddDays(1)), Ct);
        var read = await Accounts.FindAsync(ana, Ct);

        Assert.Equal("4821", first.ClabeLast4);
        Assert.Equal(("9134", Now, Now.AddDays(1)), (second.ClabeLast4, second.CreatedAt, second.UpdatedAt));
        Assert.Equal(("9134", "BBVA México"), (read?.ClabeLast4, read?.BankName));
        Assert.Null(await Accounts.FindAsync(AuthIds.New(), Ct));
        Assert.Null(await Accounts.FindAsync("no-es-un-id", Ct));
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatPersonsAccount()
    {
        var ana = AuthIds.New();
        var diego = AuthIds.New();
        await Accounts.SaveAsync(Account(ana, "4821", Now), Ct);
        await Accounts.SaveAsync(Account(diego, "9134", Now), Ct);

        Assert.True(await Accounts.DeleteAsync(ana, Ct));
        Assert.False(await Accounts.DeleteAsync(ana, Ct));

        Assert.Null(await Accounts.FindAsync(ana, Ct));
        Assert.NotNull(await Accounts.FindAsync(diego, Ct));
    }
}
