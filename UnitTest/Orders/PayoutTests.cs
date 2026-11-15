using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Modules.Orders.Data;
using Musubi.TestShared;

namespace UnitTest.Orders;

/// <summary>
/// HU-55 (maqueta <c>Cuenta-pagos</c>): la CLABE se valida con su dígito verificador y su banco de SPEI, el titular debe
/// ser la persona verificada, se guarda cifrada (AES-GCM, ligada a su dueña) y solo se muestra enmascarada. Sin llave
/// de cifrado no se guardan cuentas (RNF-15).
/// </summary>
public sealed class PayoutTests
{
    private const string Uttt = InMemoryCommunityRepository.UtttId;

    private static readonly DateTimeOffset Now = new(2026, 11, 15, 18, 0, 0, TimeSpan.Zero);

    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryPayoutAccountRepository accounts = new();
    private readonly ManualTimeProvider time = new(Now);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PayoutService Service(ClabeProtector? protector = null) =>
        new(accounts, new MemberDirectory(new InMemoryCommunityRepository(), users), protector ?? PayoutTestKit.Protector(), time);

    private async Task<string> MemberAsync(string name = "Ana Sofía Ramírez López")
    {
        var id = AuthIds.New();
        Assert.True(await users.TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = Uttt,
                Email = $"u.{id}@uttt.edu.mx",
                EmailNormalized = $"u.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = name,
                Status = UserStatuses.Active,
                EmailVerifiedAt = Now.UtcDateTime.AddDays(-30),
                CreatedAt = Now.UtcDateTime.AddDays(-30),
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    [Theory]
    [InlineData("032180000118359719", true)] // el ejemplo de Banxico
    [InlineData("032180000118359718", false)] // otro dígito verificador
    [InlineData("03218000011835971", false)] // 17 digitos
    [InlineData("0321800001183597190", false)] // 19
    [InlineData("03218000011835971a", false)]
    public void TheCheckDigit_IsBanxicos(string clabe, bool valid) => Assert.Equal(valid, Clabe.IsValid(clabe));

    [Fact]
    public void AClabe_IsReadWithSpacesOrDashes_AndShownInGroups()
    {
        Assert.Equal("012180015488214820", Clabe.Normalize(" 012 180-0154 8821 4820 "));
        Assert.Equal("012 180 0154 8821 4821", Clabe.Format("012180015488214821"));
    }

    [Theory]
    [InlineData("012180015488214821", "BBVA México")]
    [InlineData("638180000000000000", "Nu México")]
    [InlineData("722969000000000000", "Mercado Pago")]
    [InlineData("999180000000000000", null)]
    public void TheBank_ComesFromTheFirstThreeDigits(string clabe, string? bank) => Assert.Equal(bank, MexicanBanks.NameOf(clabe));

    [Theory]
    [InlineData("Ana Sofía Ramírez", true)]
    [InlineData("ANA SOFIA RAMIREZ LOPEZ", true)]
    [InlineData("ana ramírez", true)]
    [InlineData("Ana", false)] // una sola palabra
    [InlineData("Ana Sofía Hernández", false)] // otra persona
    [InlineData("Diego Hernández", false)]
    public void TheHolder_MustBeTheVerifiedPerson(string holder, bool matches) =>
        Assert.Equal(matches, HolderNames.Matches(holder, "Ana Sofía Ramírez López"));

    [Fact]
    public void TheClabe_IsEncrypted_AndOnlyItsOwnerCanReadIt()
    {
        var protector = PayoutTestKit.Protector();
        var ana = AuthIds.New();

        var once = protector.Protect(ana, PayoutTestKit.BbvaClabe);
        var twice = protector.Protect(ana, PayoutTestKit.BbvaClabe);

        Assert.StartsWith("v1:", once, StringComparison.Ordinal);
        Assert.DoesNotContain(PayoutTestKit.BbvaClabe, once, StringComparison.Ordinal);
        Assert.NotEqual(once, twice); // nonce nuevo cada vez
        Assert.Equal(PayoutTestKit.BbvaClabe, protector.Unprotect(ana, once));
        // Copiada a la cuenta de otra persona, o leida con otra llave, no se descifra.
        Assert.Null(protector.Unprotect(AuthIds.New(), once));
        Assert.Null(PayoutTestKit.Protector(Convert.ToBase64String(new byte[32])).Unprotect(ana, once));
        Assert.Null(protector.Unprotect(ana, "v1:no-es-base64"));
    }

    [Fact]
    public void WithoutAKey_TransfersAreOff_ExceptATemporaryOneInDevelopment()
    {
        Assert.False(PayoutTestKit.Protector(key: "").Enabled);
        Assert.False(PayoutTestKit.Protector(key: "no-es-una-llave").Enabled);
        Assert.False(PayoutTestKit.Protector(key: Convert.ToBase64String(new byte[16])).Enabled);
        Assert.True(PayoutTestKit.Protector(key: "", environment: "Development").Enabled);
        Assert.True(PayoutTestKit.Protector().Enabled);
    }

    [Fact]
    public async Task Save_StoresItEncrypted_AndShowsItMasked()
    {
        var ana = await MemberAsync();
        var service = Service();

        var saved = await service.SaveAsync(ana, new SavePayoutAccountRequest("  Ana  Sofía Ramírez ", "012 180 0154 8821 482" + PayoutTestKit.BbvaClabe[^1]), Ct);

        Assert.Equal(new PayoutAccountResponse("Ana Sofía Ramírez", "012", "BBVA México", PayoutTestKit.BbvaClabe[^4..], Now.UtcDateTime), saved.Value);
        var stored = await accounts.FindAsync(ana, Ct);
        Assert.DoesNotContain(PayoutTestKit.BbvaClabe, stored!.ClabeCiphertext, StringComparison.Ordinal);
        Assert.Equal(new PayoutAccountStatus(true, saved.Value), await service.MineAsync(ana, Ct));
        Assert.True(await service.CanReceiveAsync(ana, Ct));
        Assert.Equal(PayoutTestKit.BbvaClabe, (await service.ForTransferAsync(ana, Ct))?.Clabe);
    }

    [Theory]
    [InlineData("An", "012180015488214821", "payouts.invalid_holder")]
    [InlineData("Diego Hernández", null, "payouts.holder_mismatch")]
    [InlineData("Ana Ramírez", "012180015488214820", "payouts.invalid_clabe")]
    [InlineData("Ana Ramírez", "", "payouts.invalid_clabe")]
    public async Task Save_ChecksTheHolderAndTheClabe(string holder, string? clabe, string code)
    {
        var ana = await MemberAsync();
        // La CLABE valida de la maqueta, salvo los casos que prueban otra.
        var body = new SavePayoutAccountRequest(holder, clabe == "012180015488214821" || clabe is null ? PayoutTestKit.BbvaClabe : clabe);

        Assert.Equal(code, (await Service().SaveAsync(ana, body, Ct)).Error!.Code);
        Assert.Null(await accounts.FindAsync(ana, Ct));
    }

    [Fact]
    public async Task Save_WithAnUnknownBank_OrWithoutAKey_IsRefused()
    {
        var ana = await MemberAsync();
        var unknownBank = PayoutTestKit.WithCheckDigit("99918001548821482");

        Assert.Equal("payouts.unknown_bank", (await Service().SaveAsync(ana, new SavePayoutAccountRequest("Ana Ramírez", unknownBank), Ct)).Error!.Code);
        var off = Service(PayoutTestKit.Protector(key: ""));
        Assert.Equal("payouts.unavailable", (await off.SaveAsync(ana, new SavePayoutAccountRequest("Ana Ramírez", PayoutTestKit.BbvaClabe), Ct)).Error!.Code);
        Assert.False((await off.MineAsync(ana, Ct)).Available);
        Assert.Equal("auth.forbidden", (await Service().SaveAsync(AuthIds.New(), new SavePayoutAccountRequest("Ana Ramírez", PayoutTestKit.BbvaClabe), Ct)).Error!.Code);
    }

    [Fact]
    public async Task Delete_LeavesOnlyCash_AndSoDoesErasingTheAccount()
    {
        var ana = await MemberAsync();
        var diego = await MemberAsync("Diego Hernández");
        var service = Service();
        await service.SaveAsync(ana, new SavePayoutAccountRequest("Ana Ramírez", PayoutTestKit.BbvaClabe), Ct);
        await service.SaveAsync(diego, new SavePayoutAccountRequest("Diego Hernández", PayoutTestKit.BbvaClabe), Ct);

        await service.DeleteAsync(ana, Ct);
        await new PayoutAccountEraser(accounts).EraseAsync(diego, Ct);

        Assert.Null((await service.MineAsync(ana, Ct)).Account);
        Assert.False(await service.CanReceiveAsync(ana, Ct));
        Assert.Null(await service.ForTransferAsync(diego, Ct));
    }

    [Fact]
    public void TheBankList_HasTheCommonOnesFirst_WithoutRepeatedCodes()
    {
        var banks = PayoutService.Banks().Items;

        Assert.Equal(["Banamex", "BBVA México", "Santander"], banks.Take(3).Select(b => b.Name));
        Assert.Equal(banks.Count, banks.Select(b => b.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.All(banks, b => Assert.Matches("^[0-9]{3}$", b.Code));
    }
}
