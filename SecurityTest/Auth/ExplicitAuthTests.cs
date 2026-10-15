using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>RNF-03 / OWASP API5: cada controller declara su auth de forma explicita (refuerza el bloqueante B3 del critic).</summary>
public sealed class ExplicitAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    public static TheoryData<string> ApiControllers()
    {
        var data = new TheoryData<string>();
        foreach (var type in ApiAssembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
        {
            data.Add(type.FullName!);
        }

        return data;
    }

    [Fact]
    public void ApiAssembly_HasAtLeastOneController()
    {
        Assert.NotEmpty(ApiControllers());
    }

    [Theory]
    [MemberData(nameof(ApiControllers))]
    public void EveryAction_DeclaresAuthorizeOrAllowAnonymous(string controllerName)
    {
        var controller = ApiAssembly.GetType(controllerName, throwOnError: true)!;
        var classDeclares = HasAuthDeclaration(controller);

        var actions = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

        foreach (var action in actions)
        {
            Assert.True(
                classDeclares || HasAuthDeclaration(action),
                $"{controller.Name}.{action.Name} no declara [Authorize] ni [AllowAnonymous].");
        }
    }

    [Fact]
    public async Task Production_DoesNotExposeOpenApiOrScalar()
    {
        using var client = production.CreateClient();

        var openApi = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        var scalar = await client.GetAsync(new Uri("/scalar", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, openApi.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, scalar.StatusCode);
    }

    private static bool HasAuthDeclaration(MemberInfo member) =>
        member.GetCustomAttributes(inherit: true).Any(a => a is IAuthorizeData or IAllowAnonymous);
}
