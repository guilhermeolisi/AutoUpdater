using AutoUpdaterModel;
using Xunit;

namespace AutoUpdater.Tests;

/// <summary>
/// Tests for <see cref="Services.BuildAppStartInfo"/> — em especial o caso
/// macOS, onde o apphost não roda e o app deve ser iniciado via "dotnet &lt;dll&gt;".
/// </summary>
public class ServicesLaunchTests
{
    private const string Folder = "/opt/sindarin";
    private const string App = "Sindarin";

    [Fact]
    public void BuildAppStartInfo_Windows_UsesExeApphost()
    {
        var psi = Services.BuildAppStartInfo(Folder, App, 0, "\"a\" \"b\"");

        Assert.Equal(Path.Combine(Folder, App + ".exe"), psi.FileName);
        Assert.Equal("\"a\" \"b\"", psi.Arguments);
        Assert.True(psi.UseShellExecute);
    }

    [Fact]
    public void BuildAppStartInfo_Linux_UsesExtensionlessApphost()
    {
        var psi = Services.BuildAppStartInfo(Folder, App, 1, null);

        Assert.Equal(Path.Combine(Folder, App), psi.FileName);
        Assert.Equal(string.Empty, psi.Arguments);
        Assert.True(psi.UseShellExecute);
    }

    [Fact]
    public void BuildAppStartInfo_MacOS_LaunchesViaDotnetDll()
    {
        var psi = Services.BuildAppStartInfo(Folder, App, 2, "\"a\" \"b\"");

        Assert.Equal("dotnet", psi.FileName);
        Assert.Equal($"\"{Services.AppDllPath(Folder, App)}\" \"a\" \"b\"", psi.Arguments);
        Assert.False(psi.UseShellExecute);
    }

    [Fact]
    public void BuildAppStartInfo_MacOS_WithoutArgs_PassesOnlyDll()
    {
        var psi = Services.BuildAppStartInfo(Folder, App, 2, null);

        Assert.Equal("dotnet", psi.FileName);
        Assert.Equal($"\"{Services.AppDllPath(Folder, App)}\"", psi.Arguments);
    }

    [Fact]
    public void AppDllPath_AppendsDllExtension()
    {
        Assert.Equal(Path.Combine(Folder, App + ".dll"), Services.AppDllPath(Folder, App));
    }
}
