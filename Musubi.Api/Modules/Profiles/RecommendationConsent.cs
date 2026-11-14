using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Profiles.Data;

namespace Musubi.Api.Modules.Profiles;

/// <summary>
/// Contrato de M-02 para M-11 (HU-35): si la persona acepta "Recomendarte productos según tu actividad" (finalidad
/// adicional del aviso de privacidad, HU-47). Sin preferencia guardada, si.
/// </summary>
internal interface IRecommendationConsent
{
    /// <summary>Si se puede usar su actividad.</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si acepta.</returns>
    Task<bool> AllowsAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre las preferencias (HU-54).</summary>
internal sealed class RecommendationConsent(IPreferenceRepository preferences) : IRecommendationConsent
{
    /// <inheritdoc />
    public async Task<bool> AllowsAsync(string userId, CancellationToken cancellationToken) =>
        (await preferences.FindAsync(userId, cancellationToken).ConfigureAwait(false))?.Recommendations ?? true;
}
