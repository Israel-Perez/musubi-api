using Musubi.Api.Modules.Auth;

namespace UnitTest.Auth;

/// <summary>HU-01 / HU-03: correo normalizado, codigos de verificacion y correo sin inyeccion de HTML.</summary>
public sealed class RegistrationTests
{
    [Theory]
    [InlineData("Ana.Ramirez@UTTT.edu.mx", "ana.ramirez@uttt.edu.mx", "uttt.edu.mx")]
    [InlineData("  tu.nombre@uttt.edu.mx ", "tu.nombre@uttt.edu.mx", "uttt.edu.mx")]
    public void Email_IsNormalizedWithItsDomain(string input, string address, string domain)
    {
        Assert.True(EmailAddresses.TryNormalize(input, out var email));
        Assert.Equal(address, email.Address);
        Assert.Equal(domain, email.Domain);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sin-arroba")]
    [InlineData("Ana <ana@uttt.edu.mx>")]
    [InlineData("ana@localhost")]
    public void Email_RejectsMalformedAddresses(string? input)
    {
        Assert.False(EmailAddresses.TryNormalize(input, out _));
    }

    [Fact]
    public void Code_HasSixDigitsAndVaries()
    {
        var codes = Enumerable.Range(0, 50).Select(_ => VerificationCodes.Generate()).ToList();

        Assert.All(codes, c => Assert.Matches("^[0-9]{6}$", c));
        Assert.True(codes.Distinct(StringComparer.Ordinal).Count() > 40);
    }

    [Fact]
    public void Code_HashIsBoundToTheUser()
    {
        var hash = VerificationCodes.Hash("user-a", "123456");

        Assert.Equal(64, hash.Length);
        Assert.NotEqual(hash, VerificationCodes.Hash("user-b", "123456"));
        Assert.True(VerificationCodes.Matches(hash, "user-a", "123456"));
        Assert.False(VerificationCodes.Matches(hash, "user-a", "123457"));
        Assert.False(VerificationCodes.Matches(hash, "user-b", "123456"));
    }

    [Fact]
    public void VerificationEmail_CarriesTheCodeAndEncodesTheName()
    {
        var message = AuthEmails.VerificationCode("ana@uttt.edu.mx", "<script>alert(1)</script> Ramírez", "482173");

        Assert.Equal("ana@uttt.edu.mx", message.To);
        Assert.Contains("482173", message.Subject, StringComparison.Ordinal);
        Assert.Contains("482173", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Vence en 10 minutos", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", message.HtmlBody, StringComparison.Ordinal);
    }
}
