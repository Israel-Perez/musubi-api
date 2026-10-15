using System.Reflection;

namespace UnitTest.Common;

/// <summary>01-01: el ensamblado principal carga y expone <c>Program</c>.</summary>
public sealed class BuildSmokeTests
{
    [Fact]
    public void MainAssembly_Loads_WithoutTypeLoadErrors()
    {
        var asm = Assembly.Load("Musubi.Api");

        var types = asm.GetTypes();

        Assert.NotEmpty(types);
    }

    [Fact]
    public void MainAssembly_ContainsProgram()
    {
        var asm = Assembly.Load("Musubi.Api");

        Assert.Contains(asm.GetTypes(), t => t.Name == "Program");
    }
}
