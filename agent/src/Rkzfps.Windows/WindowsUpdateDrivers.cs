using Rkzfps.Core.Diagnostics;

namespace Rkzfps.Windows;

/// <summary>
/// Pergunta ao Windows Update, pela interface oficial dele (Windows Update
/// Agent), quais drivers novos existem para este PC. Só LÊ: procurar não pede
/// administrador e não muda nada.
///
/// Instalar (Install) também vai pelo Windows Update, no processo elevado e
/// só com o id que a busca devolveu: o driver é o que a Microsoft distribui
/// para este PC, nunca um arquivo baixado de outro lugar. O desfazer é o do
/// Windows (ponto de restauração e Reverter driver).
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
                SafeDate(() => (DateTime)u.DriverVerDate))
            {
                UpdateId = Safe(() => (string)u.Identity.UpdateID),
            });
        }
        return list;
    }

    /// <summary>Busca de novo pelo id (a oferta pode ter mudado), baixa e instala. Precisa de administrador.</summary>
    public static Rkzfps.Core.Engine.CommandResult Install(string updateId)
    {
        if (!Guid.TryParse(updateId, out var id))
            return new(2, "Id de atualização inválido.");
        var type = Type.GetTypeFromProgID("Microsoft.Update.Session")
                   ?? throw new InvalidOperationException("O Windows Update não está disponível neste PC.");
        dynamic session = Activator.CreateInstance(type)!;
        session.ClientApplicationID = "RKZFPS";
        dynamic result = session.CreateUpdateSearcher().Search($"UpdateID='{id:D}' and IsInstalled=0 and Type='Driver'");
        if ((int)result.Updates.Count == 0)
            return new(3, "O Windows Update não oferece mais este driver (já instalado ou substituído).");

        dynamic update = result.Updates.Item(0);
        if (!(bool)update.EulaAccepted)
            update.AcceptEula();
        dynamic updates = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")!)!;
        updates.Add(update);

        dynamic downloader = session.CreateUpdateDownloader();
        downloader.Updates = updates;
        dynamic download = downloader.Download();
        // 2 = concluído, 3 = concluído com erros (OperationResultCode do WUA).
        if ((int)download.ResultCode is not (2 or 3))
            return new(4, $"O download falhou (código {(int)download.ResultCode} do Windows Update).");

        dynamic installer = session.CreateUpdateInstaller();
        installer.Updates = updates;
        dynamic install = installer.Install();
        var code = (int)install.ResultCode;
        if (code is not (2 or 3))
            return new(5, $"A instalação falhou (código {code} do Windows Update).");
        return new(0, (bool)install.RebootRequired ? "Driver instalado. Reinicie o PC para concluir." : "Driver instalado.");
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
