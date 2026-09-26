using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Fpsx.Core.Model;

namespace Fpsx.App;

public sealed class BoolToVisibility : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value switch
        {
            bool v => v,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i > 0,
            null => false,
            _ => true,
        };
        return b ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public static class StatusStyle
{
    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    public static (string Label, string Brush) Of(HealthStatus s) => s switch
    {
        HealthStatus.Ok => ("OK", "Accent"),
        HealthStatus.Info => ("INFO", "Info"),
        HealthStatus.Attention => ("ATENÇÃO", "Warn"),
        HealthStatus.Problem => ("PROBLEMA", "Danger"),
        _ => ("?", "Muted"),
    };

    public static (string Label, string Brush) Of(Decision d) => d switch
    {
        Decision.Recommended => ("RECOMENDADA", "Warn"),
        Decision.Optional => ("OPCIONAL", "Info"),
        Decision.AlreadyOptimal => ("JÁ OTIMIZADO", "Accent"),
        Decision.NotApplicable => ("NÃO SE APLICA", "Muted"),
        Decision.Blocked => ("BLOQUEADA", "Muted"),
        _ => ("DESCONHECIDO", "Muted"),
    };

    public static string Potential(Potential p) => p switch
    {
        Fpsx.Core.Model.Potential.High => "Potencial alto",
        Fpsx.Core.Model.Potential.Moderate => "Potencial moderado",
        Fpsx.Core.Model.Potential.Low => "Potencial baixo",
        _ => "Troubleshooting (não aumenta FPS)",
    };
}
