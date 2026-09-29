using Fpsx.Core.Diagnostics;

namespace Fpsx.Windows;

/// <summary>
/// Pergunta ao Windows Update, pela interface oficial dele (Windows Update
/// Agent), quais drivers novos existem para este PC. Só LÊ: procurar não pede
/// administrador e não muda nada.
///
/// A instalação fica com o Windows Update (o app abre a tela certa), porque o
/// RKZFPS só aplica o que sabe desfazer, e driver instalado não tem desfazer
/// pelo app. O Windows Update tem o controle e a reversão de driver dele.
/// </summary>
public static class WindowsUpdateDrivers
{
    /// <summary>Busca pode levar de alguns segundos a um minuto: roda fora da tela.</summary>
    public static Task<IReadOnlyList<DriverUpdate>> SearchAsync(CancellationToken ct = default) =>
        Task.Run(() => Search(), ct);

    private static IReadOnlyList<DriverUpdate> Search()
    {
        var type = Type.GetTypeFromProgID("Microsoft.Update.Session")
                   ?? throw new InvalidOperationException("O Windows Update não está disponível neste PC.");
        dynamic session = Activator.CreateInstance(type)!;
        session.ClientApplicationID = "RKZFPS";
        dynamic searcher = session.CreateUpdateSearcher();
        // Só drivers que ainda não estão instalados e não estão ocultos pela pessoa.
        dynamic result = searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Driver'");

        var list = new List<DriverUpdate>();
        int count = result.Updates.Count;
        for (var i = 0; i < count; i++)
        {
            dynamic u = result.Updates.Item(i);
            list.Add(new DriverUpdate(
                (string)u.Title,
                Safe(() => (string)u.DriverClass),
                Safe(() => (string)u.DriverManufacturer),
                SafeDate(() => (DateTime)u.DriverVerDate)));
        }
        return list;
    }

    private static string Safe(Func<string> read)
    {
        try
        {
            return read() ?? "";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return "";
        }
    }

    private static DateTime? SafeDate(Func<DateTime> read)
    {
        try
        {
            var d = read();
            return d.Year > 1990 ? d : null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException)
        {
            return null;
        }
    }
}
