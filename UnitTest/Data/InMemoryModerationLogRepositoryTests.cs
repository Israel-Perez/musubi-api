using Musubi.Api.Modules.Admin.Data;
using Musubi.TestShared.Admin;

namespace UnitTest.Data;

/// <summary>HU-39/40: la bitacora en memoria cumple el mismo contrato que MongoDB (ver DatabaseTest).</summary>
public sealed class InMemoryModerationLogRepositoryTests : ModerationLogRepositoryContract
{
    internal override IModerationLogRepository Log { get; } = new InMemoryModerationLogRepository();
}
