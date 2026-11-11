using Musubi.Api.Modules.GenAi.Data;
using Musubi.TestShared.GenAi;

namespace UnitTest.Data;

/// <summary>HU-32/33/34: el fake en memoria cumple el mismo contrato que MongoDB (ver DatabaseTest).</summary>
public sealed class InMemoryAgreementRepositoryTests : AgreementRepositoryContract
{
    internal override IAgreementRepository Agreements { get; } = new InMemoryAgreementRepository();
}
