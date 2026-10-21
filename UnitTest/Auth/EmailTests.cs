using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Musubi.Api.Platform.Email;
using Musubi.Api.Platform.Errors;

namespace UnitTest.Auth;

/// <summary>02-03: envio de correo (SES fuera de Development) y codigos genericos de error.</summary>
public sealed class EmailTests
{
    [Theory]
    [InlineData("Development", "Log", "", true)]
    [InlineData("Production", "Log", "", false)]
    [InlineData("Production", "Ses", "", false)]
    [InlineData("Production", "Ses", "Musubi <no-reply@musubi.example>", true)]
    [InlineData("Production", "Smtp", "x@y.z", false)]
    public void Options_OnlyAllowTheLogProviderInDevelopment(string environment, string provider, string from, bool valid)
    {
        var validator = new EmailOptionsValidator(new FakeEnvironment(environment));

        var result = validator.Validate(null, new EmailOptions { Provider = provider, From = from });

        Assert.Equal(valid, result.Succeeded);
    }

    [Fact]
    public async Task Ses_MapsTheMessageToAUtf8Request()
    {
        var client = new RecordingSesClient();
        var sender = new SesEmailSender(client, Options.Create(new EmailOptions { From = "Musubi <no-reply@musubi.example>" }));

        await sender.SendAsync(new EmailMessage("ana@uttt.edu.mx", "Tu código", "Código: 123456", "<p>123456</p>"), TestContext.Current.CancellationToken);

        var request = Assert.Single(client.Requests);
        Assert.Equal("Musubi <no-reply@musubi.example>", request.FromEmailAddress);
        Assert.Equal(["ana@uttt.edu.mx"], request.Destination.ToAddresses);
        Assert.Equal("Tu código", request.Content.Simple.Subject.Data);
        Assert.Equal("Código: 123456", request.Content.Simple.Body.Text.Data);
        Assert.Equal("UTF-8", request.Content.Simple.Body.Html.Charset);
    }

    [Theory]
    [InlineData(400, "validation.failed")]
    [InlineData(401, "auth.unauthorized")]
    [InlineData(403, "auth.forbidden")]
    [InlineData(404, "resource.not_found")]
    [InlineData(429, "rate_limit.exceeded")]
    [InlineData(500, "server.error")]
    [InlineData(502, "server.error")]
    public void GenericErrors_HaveAStableCodeAndASpanishMessage(int status, string code)
    {
        var error = ApiErrors.ForStatus(status);

        Assert.Equal(code, error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    private sealed class RecordingSesClient : ISesClient
    {
        public List<SendEmailRequest> Requests { get; } = [];

        public Task SendAsync(SendEmailRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Musubi.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
