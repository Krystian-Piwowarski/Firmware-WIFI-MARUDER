using System.Globalization;
using Rcp.Core;
using Rcp.Core.Services;
using Rcp.App.Forms;

namespace Rcp.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // jedna instancja na sesję – dwie kopie walczyłyby o te same czytniki
        using var mutex = new Mutex(true, @"Local\EwidencjaRCP_SingleInstance", out bool created);
        if (!created)
        {
            MessageBox.Show("Program Ewidencja RCP jest już uruchomiony.", "Ewidencja RCP", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // daty, nazwy dni i miesięcy po polsku niezależnie od języka systemu
        var pl = CultureInfo.GetCultureInfo("pl-PL");
        CultureInfo.DefaultThreadCurrentCulture = pl;
        CultureInfo.DefaultThreadCurrentUICulture = pl;
        Thread.CurrentThread.CurrentCulture = pl;
        Thread.CurrentThread.CurrentUICulture = pl;

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Ui.ShowError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Nieobsłużony wyjątek", e.ExceptionObject as Exception);

        var config = AppConfig.Load(args);
        Log.Directory = Path.Combine(AppConfig.DataDirectory, "logs");
        Log.Info($"Start aplikacji, baza: {config.DatabasePath}");

        RcpContext ctx;
        try
        {
            ctx = new RcpContext(config.DatabasePath);
        }
        catch (Exception ex)
        {
            Log.Error("Nie można otworzyć bazy", ex);
            MessageBox.Show($"Nie można otworzyć bazy danych:\n{config.DatabasePath}\n\n{ex.Message}",
                "Ewidencja RCP", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.Run(new MainForm(ctx, config));
        Log.Info("Zamknięcie aplikacji");
    }
}
