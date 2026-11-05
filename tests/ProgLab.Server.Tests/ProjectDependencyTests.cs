using System.Xml.Linq;

namespace ProgLab.Server.Tests;

/// <summary>
/// Tasarımdaki bağımlılık kurallarını csproj dosyalarından doğrular:
/// Shared hiçbir projeye bağlı değildir; Server ve Client.Core yalnızca Shared'a,
/// iki masaüstü uygulaması yalnızca Client.Core'a bağlıdır.
/// </summary>
public sealed class ProjectDependencyTests
{
    [Theory]
    [InlineData("ProgLab.Shared")]
    [InlineData("ProgLab.Server", "ProgLab.Shared")]
    [InlineData("ProgLab.Client.Core", "ProgLab.Shared")]
    [InlineData("ProgLab.InstructorApp", "ProgLab.Client.Core")]
    [InlineData("ProgLab.StudentApp", "ProgLab.Client.Core")]
    public void Project_ReferencesOnlyAllowedProjects(string project, params string[] allowed)
    {
        var csproj = Path.Combine(FindSolutionRoot(), "src", project, $"{project}.csproj");

        var references = XDocument.Load(csproj)
            .Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value.Replace('\\', '/')))
            .Order()
            .ToArray();

        Assert.Equal(allowed.Order().ToArray(), references);
    }

    [Fact]
    public void FinalProjects_DoNotExistInMidtermVersion()
    {
        var root = FindSolutionRoot();

        Assert.False(Directory.Exists(Path.Combine(root, "src", "ProgLab.Runner")));
        Assert.False(Directory.Exists(Path.Combine(root, "tests", "ProgLab.Runner.Tests")));
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProgLab.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("ProgLab.sln bulunamadı.");
    }
}
