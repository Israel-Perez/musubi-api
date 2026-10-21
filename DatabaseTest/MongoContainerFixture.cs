using System.Collections.Concurrent;
using System.Text;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;
using Musubi.TestShared;
using Testcontainers.MongoDb;

[assembly: AssemblyFixture(typeof(DatabaseTest.MongoContainerFixture))]

namespace DatabaseTest;

/// <summary>
/// 02-01: un MongoDB real (misma imagen que deploy/docker-compose.local.yml) para toda la suite.
/// Cada clase de prueba pide su propia base y la fixture le aplica los scripts reales de db/ con mongosh.
/// </summary>
public sealed class MongoContainerFixture : IAsyncLifetime
{
    private const string Image = "mongo:8.0.32";
    private const string User = "musubi";
    private const string Password = "solo-pruebas-en-contenedor";

    private readonly MongoDbContainer container = new MongoDbBuilder(Image)
        .WithUsername(User)
        .WithPassword(Password)
        .Build();

    private readonly ConcurrentDictionary<string, Lazy<Task<IMongoDatabase>>> databases = new(StringComparer.Ordinal);
    private MongoClient? client;

    public async ValueTask InitializeAsync()
    {
        MongoConventions.Register();
        await container.StartAsync().ConfigureAwait(false);
        client = new MongoClient(container.GetConnectionString());
    }

    public async ValueTask DisposeAsync()
    {
        client?.Dispose();
        await container.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Base nueva sin scripts aplicados.</summary>
    public IMongoDatabase EmptyDatabase(string name) => client!.GetDatabase(name);

    /// <summary>Base con todos los scripts de db/ aplicados (una sola vez por nombre).</summary>
    public Task<IMongoDatabase> MigratedDatabaseAsync(string name) =>
        databases.GetOrAdd(name, n => new Lazy<Task<IMongoDatabase>>(async () =>
        {
            await ApplyScriptsAsync(n).ConfigureAwait(false);
            return client!.GetDatabase(n);
        })).Value;

    /// <summary>Aplica en orden cada script de db/ con mongosh dentro del contenedor.</summary>
    public async Task ApplyScriptsAsync(string databaseName)
    {
        foreach (var script in Directory.GetFiles(RepoRoot.File("db"), "*.js").Order(StringComparer.Ordinal))
        {
            var target = $"/tmp/db/{Path.GetFileName(script)}";
            var content = await File.ReadAllTextAsync(script).ConfigureAwait(false);
            await container.CopyAsync(Encoding.UTF8.GetBytes(content), target).ConfigureAwait(false);
            var result = await container.ExecAsync(
            [
                "mongosh", "--quiet",
                $"mongodb://{User}:{Password}@localhost:27017/{databaseName}?authSource=admin",
                "--file", target,
            ]).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"{Path.GetFileName(script)} fallo ({result.ExitCode}): {result.Stderr}{result.Stdout}");
            }
        }
    }
}
