namespace ProgLab.Server.Tests.Infrastructure;

public static class SolutionPaths
{
    /// <summary>ProgLab.sln dosyasının bulunduğu klasör.</summary>
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProgLab.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("ProgLab.sln bulunamadı.");
    }
}
