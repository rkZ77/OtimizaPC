using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Fpsx.Client;
using Fpsx.Core.Engine;

namespace Fpsx.App;

/// <summary>
/// Diálogos no tema do app. Erro técnico (stack, "Object reference") nunca
/// chega ao usuário: vai para o log local e a tela mostra texto em pt-BR.
/// </summary>
public static class Dialogs
{
    public static int Show(string title, string message, params string[] buttons)
    {
        var owner = Application.Current?.MainWindow;
        var result = -1;
        var window = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 640,
            WindowStartupLocation = owner is { IsLoaded: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner is { IsLoaded: true } ? owner : null,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)Application.Current!.Resources["Surface"],
            ShowInTaskbar = owner is null,
        };

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["H2"] });
        root.Children.Add(new ScrollViewer
        {
            MaxHeight = 420,
            Content = new TextBlock { Text = message, Margin = new Thickness(0, 4, 0, 20), LineHeight = 21 },
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var button = new Button
            {
                Content = buttons[i],
                Margin = new Thickness(8, 0, 0, 0),
                Style = (Style)Application.Current.Resources[i == 0 ? "Primary" : "BaseButton"],
                IsDefault = i == 0,
                IsCancel = i == buttons.Length - 1 && buttons.Length > 1,
            };
            button.Click += (_, _) => { result = index; window.Close(); };
            row.Children.Add(button);
        }

        root.Children.Add(row);
        window.Content = root;
        window.ShowDialog();
        return result;
    }

    public static void Info(string title, string message) => Show(title, message, "OK");

    public static bool Confirm(string title, string message, string confirm = "Continuar") => Show(title, message, confirm, "Cancelar") == 0;

    /// <summary>Fail-safe da seção 43: uma alteração falhou e o usuário decide.</summary>
    public static FailureChoice Failure(FailureContext f) =>
        Show("A alteração falhou",
            $"\"{f.Description}\" falhou:\n{f.Error}\n\nNenhuma alteração adicional será aplicada sem sua decisão.",
            "Restaurar tudo desta sessão", "Continuar com as próximas", "Parar aqui") switch
        {
            1 => FailureChoice.Continue,
            2 => FailureChoice.Cancel,
            _ => FailureChoice.Restore,
        };

    public static void Error(Exception ex)
    {
        var message = ex switch
        {
            ApiException api => api.Message,
            SafetyViolationException s => "O FPSX recusou a operação por segurança: " + s.Message,
            InvalidOperationException or FileNotFoundException or ArgumentException => ex.Message,
            UnauthorizedAccessException => "O Windows negou acesso. Algumas otimizações exigem abrir o FPSX como administrador.",
            _ => "Algo deu errado. Nenhuma alteração foi deixada pela metade: o que já foi aplicado está no Histórico e pode ser desfeito.",
        };
        Log(ex);
        Info("Não foi possível concluir", message);
    }

    public static void Log(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FPSX", "log");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "app-errors.log"), $"{DateTimeOffset.Now:O} {ex}\n\n");
        }
        catch (IOException)
        {
        }
    }
}
