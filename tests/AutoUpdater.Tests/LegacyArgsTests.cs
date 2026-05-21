using AutoUpdaterModel;
using Xunit;

namespace AutoUpdater.Tests;

/// <summary>
/// Tolerância a linhas de comando legadas/mal-formadas: hosts antigos do
/// Sindarin embrulhavam o caminho de instalação terminado em '\', corrompido
/// pelo CommandLineToArgvW do Windows. O updater novo deve recuperar.
/// </summary>
public class LegacyArgsTests
{
    [Fact]
    public void TokenizeArgsLenient_SkipsExecutable_AndSplitsQuotedTokens()
    {
        string cmd = "\"C:\\app\\AutoUpdaterConsole.exe\" \"a\" \"b c\" \"d\"";

        string[] tokens = Services.TokenizeArgsLenient(cmd);

        Assert.Equal(new[] { "a", "b c", "d" }, tokens);
    }

    [Fact]
    public void TokenizeArgsLenient_RecoversFolderEndingInBackslash()
    {
        // Caminho terminando em '\' antes da aspa de fechamento (o caso que quebra).
        string cmd = "\"C:\\app\\AutoUpdater\\AutoUpdaterConsole.exe\" " +
                     "\"0.1.94.0\" \"0.1.95.0\" \"https://x/v.json\" " +
                     "\"C:\\app\\Sindarin-windows-x64\\\" \"\" \"Sindarin\" \"1234\"";

        string[] tokens = Services.TokenizeArgsLenient(cmd);

        Assert.Equal(7, tokens.Length);
        Assert.Equal("C:\\app\\Sindarin-windows-x64\\", tokens[3]);
        Assert.Equal(string.Empty, tokens[4]);
        Assert.Equal("Sindarin", tokens[5]);
        Assert.Equal("1234", tokens[6]);
    }

    [Fact]
    public void ProcessArg_RecoversLegacyMangledCommandLine()
    {
        string cmd = "\"C:\\app\\AutoUpdater\\AutoUpdaterConsole.exe\" " +
                     "\"0.1.94.0\" \"0.1.95.0\" \"https://x/v.json\" " +
                     "\"C:\\app\\Sindarin-windows-x64\\\" \"\" \"Sindarin\" \"1234\"";

        string[] recovered = Services.TokenizeArgsLenient(cmd);
        string err = Services.ProcessArg(recovered,
            out var oldV, out var newV, out var url, out var folder,
            out var email, out var name, out var pid, out var relaunch);

        Assert.Null(err);
        Assert.Equal(new Version(0, 1, 94, 0), oldV);
        Assert.Equal(new Version(0, 1, 95, 0), newV);
        Assert.Equal("https://x/v.json", url);
        Assert.Equal("C:\\app\\Sindarin-windows-x64\\", folder);
        Assert.Equal(string.Empty, email);
        Assert.Equal("Sindarin", name);
        Assert.Equal(1234, pid);
        Assert.True(relaunch); // 7 args legados => relaunch padrão
    }

    [Fact]
    public void TokenizeArgsLenient_HandlesEighthRelaunchArg()
    {
        string cmd = "\"updater.exe\" \"1.0.0\" \"1.1.0\" \"u\" \"C:\\x\\\" \"\" \"App\" \"10\" \"0\"";

        string[] recovered = Services.TokenizeArgsLenient(cmd);
        string err = Services.ProcessArg(recovered,
            out _, out _, out _, out var folder, out _, out _, out var pid, out var relaunch);

        Assert.Null(err);
        Assert.Equal("C:\\x\\", folder);
        Assert.Equal(10, pid);
        Assert.False(relaunch);
    }

    [Fact]
    public void TokenizeArgsLenient_EmptyCommandLine_ReturnsEmpty()
    {
        Assert.Empty(Services.TokenizeArgsLenient(""));
        Assert.Empty(Services.TokenizeArgsLenient(null));
    }
}
