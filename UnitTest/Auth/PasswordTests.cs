using System.Buffers.Binary;
using Musubi.Api.Modules.Auth;

namespace UnitTest.Auth;

/// <summary>02-03 / RNF-01: politica de la maqueta y hash adaptativo de contrasenas.</summary>
public sealed class PasswordTests
{
    private static readonly PasswordHashing Hashing = new();

    [Theory]
    [InlineData("abc12345", true)]
    [InlineData("Contraseña1", true)]
    [InlineData("abcdefgh", false)]  // sin numero
    [InlineData("12345678", false)]  // sin letra
    [InlineData("abc1234", false)]   // 7 caracteres
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Policy_FollowsTheMockup(string? password, bool valid)
    {
        Assert.Equal(valid, PasswordPolicy.IsValid(password));
    }

    [Fact]
    public void Policy_RejectsHugePasswords()
    {
        Assert.False(PasswordPolicy.IsValid("a1" + new string('x', PasswordPolicy.MaxLength)));
    }

    [Fact]
    public void Hash_IsSaltedAndVerifies()
    {
        var first = Hashing.Hash("abc12345");
        var second = Hashing.Hash("abc12345");

        Assert.NotEqual(first, second);
        Assert.DoesNotContain("abc12345", first, StringComparison.Ordinal);
        Assert.Equal(PasswordCheck.Success, Hashing.Verify(first, "abc12345"));
        Assert.Equal(PasswordCheck.Failed, Hashing.Verify(first, "abc12346"));
        Assert.Equal(PasswordCheck.Failed, Hashing.Verify(string.Empty, "abc12345"));
    }

    [Fact]
    public void Hash_UsesPbkdf2Sha512WithOwaspIterations()
    {
        // Formato Identity V3: [0x01][prf u32][iteraciones u32][largo sal u32][sal][subclave], big-endian.
        var bytes = Convert.FromBase64String(Hashing.Hash("abc12345"));

        Assert.Equal(0x01, bytes[0]);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1)));  // HMACSHA512
        Assert.Equal((uint)PasswordHashing.Iterations, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5)));
        Assert.True(PasswordHashing.Iterations >= 210_000);
    }
}
