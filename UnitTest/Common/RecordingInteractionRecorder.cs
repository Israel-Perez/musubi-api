using Musubi.Api.Modules.Recommendations;

namespace UnitTest.Common;

/// <summary>Registro de interacciones falso: guarda cada senal que dan los servicios (HU-35).</summary>
internal sealed class RecordingInteractionRecorder : IInteractionRecorder
{
    public List<Interaction> Recorded { get; } = [];

    public Task RecordAsync(Interaction interaction, CancellationToken cancellationToken)
    {
        Recorded.Add(interaction);
        return Task.CompletedTask;
    }
}
