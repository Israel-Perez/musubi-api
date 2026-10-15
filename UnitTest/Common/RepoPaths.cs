namespace UnitTest.Common;

/// <summary>Localiza la raiz del repo (carpeta que contiene <c>Musubi.slnx</c>).</summary>
internal static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string ApiFile(string fileName) => Path.Combine(Root, "Musubi.Api", fileName);

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Musubi.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("No se encontro la raiz del repo (Musubi.slnx).");
    }
}
