namespace Musubi.TestShared;

/// <summary>Localiza la raiz del repo (carpeta que contiene <c>Musubi.slnx</c>).</summary>
internal static class RepoRoot
{
    public static string Path { get; } = Find();

    public static string File(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Musubi.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("No se encontro la raiz del repo (Musubi.slnx).");
    }
}
