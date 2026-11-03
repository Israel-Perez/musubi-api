using Musubi.Api.Modules.Auth;

namespace MutationTest;

/// <summary>
/// SCRUM-94: reglas del nucleo de autenticacion con sus bordes exactos, para que Stryker mate cada mutante
/// (un limite cambiado, una condicion invertida o una comparacion sin mayusculas).
/// </summary>
public sealed class AuthRulesTests
{
    private static readonly PasswordHashing Hashing = new();

    [Theory]
    [InlineData("abcdefg1", true)]                 // 8: el minimo
    [InlineData("abcdef1", false)]                 // 7: uno menos
    [InlineData("12345678", false)]                // sin letra
    [InlineData("abcdefgh", false)]                // sin numero
    [InlineData("ñandú2026", true)]                // letras con acento cuentan
    [InlineData("", false)]
    [InlineData(null, false)]
    public void PasswordPolicy_FollowsTheMockupRules(string? password, bool valid)
    {
        Assert.Equal(valid, PasswordPolicy.IsValid(password));
    }

    [Fact]
    public void PasswordPolicy_AcceptsExactly128_AndRejects129()
    {
        Assert.True(PasswordPolicy.IsValid("a1" + new string('x', PasswordPolicy.MaxLength - 2)));
        Assert.False(PasswordPolicy.IsValid("a1" + new string('x', PasswordPolicy.MaxLength - 1)));
        Assert.Equal(8, PasswordPolicy.MinLength);
        Assert.Equal(128, PasswordPolicy.MaxLength);
    }

    [Fact]
    public void Hash_VerifiesOnlyTheSamePassword_AndIsSaltedEachTime()
    {
        var first = Hashing.Hash("musubi2026");
        var second = Hashing.Hash("musubi2026");

        Assert.NotEqual(first, second);
        Assert.Equal(PasswordCheck.Success, Hashing.Verify(first, "musubi2026"));
        Assert.Equal(PasswordCheck.Failed, Hashing.Verify(first, "musubi2027"));
        Assert.Equal(PasswordCheck.Failed, Hashing.Verify(first, "MUSUBI2026"));
    }

    [Fact]
    public void Hash_UsesIdentityV3_WithHmacSha512And210000Iterations()
    {
        // Formato Identity V3: 0x01, PRF (4 bytes), iteraciones (4 bytes), largo de la sal, sal, subllave.
        // Instancia propia: el mutante del inicializador se activa al crearla (no sirve la estatica).
        var bytes = Convert.FromBase64String(new PasswordHashing().Hash("musubi2026"));

        Assert.Equal(0x01, bytes[0]);
        Assert.Equal(2, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(1))); // HMACSHA512
        Assert.Equal(PasswordHashing.Iterations, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(5)));
        Assert.Equal(210_000, PasswordHashing.Iterations);
    }

    [Fact]
    public void Hash_OfAnEmptyPassword_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => Hashing.Hash(string.Empty));
    }

    [Fact]
    public void Verify_WithoutHashOrPassword_FailsBeforeTheHasher()
    {
        var hash = Hashing.Hash("musubi2026");

        Assert.Equal(PasswordCheck.Failed, Hashing.Verify(hash, string.Empty));
        Assert.Equal(PasswordCheck.Failed, Hashing.Verify(string.Empty, "musubi2026"));
        Assert.Equal(PasswordCheck.Failed, Hashing.Verify(null!, "musubi2026"));
    }

    [Fact]
    public void Verify_WithAGarbageHash_FailsWithoutThrowing()
    {
        Assert.Equal(PasswordCheck.Failed, Hashing.Verify("no-es-un-hash", "musubi2026"));
        Hashing.VerifyAgainstDecoy(null);
        Hashing.VerifyAgainstDecoy("musubi2026");
    }

    [Theory]
    [InlineData("  Ana.Ramirez@UTTT.edu.MX ", "ana.ramirez@uttt.edu.mx", "uttt.edu.mx")]
    [InlineData("a@musubiapp.com.mx", "a@musubiapp.com.mx", "musubiapp.com.mx")]
    public void Email_IsNormalized_AndItsDomainExtracted(string input, string address, string domain)
    {
        Assert.True(EmailAddresses.TryNormalize(input, out var email));
        Assert.Equal(address, email.Address);
        Assert.Equal(domain, email.Domain);
    }

    [Theory]
    [InlineData("Ana <ana@uttt.edu.mx>")]          // nombre para mostrar
    [InlineData("ana@localhost")]                  // dominio sin punto
    [InlineData("no-es-correo")]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    public void Email_Invalid_IsRejected(string? input)
    {
        Assert.False(EmailAddresses.TryNormalize(input, out var email));
        Assert.Null(email);
    }

    [Fact]
    public void Email_LongerThan254_IsRejected_And254IsAccepted()
    {
        var local = new string('a', 64);
        // 64 + "@" + 189 = 254: tres etiquetas de 60, una de 3 y ".mx".
        var domain254 = string.Join('.', Enumerable.Repeat(new string('b', 60), 3)) + "." + new string('c', 3) + ".mx";
        var at254 = $"{local}@{domain254}";
        Assert.Equal(254, at254.Length);

        Assert.True(EmailAddresses.TryNormalize(at254, out _));
        Assert.False(EmailAddresses.TryNormalize("x" + at254, out _));
    }
}
