namespace AutoUpdaterModel;

/// <summary>
/// Barra de progresso de console minimalista (BCL puro, AOT-safe). Substitui
/// ConsoleUtility.WriteProgressBar de BaseLibrary no fluxo do AutoUpdater.
/// </summary>
public static class ConsoleProgress
{
    private const int BlockCount = 30;

    /// <param name="percent">0..100.</param>
    /// <param name="update">
    /// true reescreve a linha atual (usa CR); false inicia uma nova barra.
    /// </param>
    public static void WriteProgressBar(int percent, bool update = false)
    {
        if (percent < 0) percent = 0;
        if (percent > 100) percent = 100;

        int filled = (int)Math.Round(BlockCount * (percent / 100d));
        string bar = "[" + new string('#', filled) + new string('-', BlockCount - filled) + $"] {percent,3}%";

        if (update && !Console.IsOutputRedirected)
            Console.Write('\r' + bar);
        else
            Console.Write(bar);
    }
}
