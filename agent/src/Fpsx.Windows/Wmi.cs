using System.Management;

namespace Fpsx.Windows;

internal static class Wmi
{
    /// <summary>
    /// Consulta WMI que devolve lista vazia em vez de exceção: classe ausente
    /// (Windows N, WMI corrompido, sem permissão) vira "desconhecido" no scan,
    /// não um crash do Agent.
    /// </summary>
    public static List<Dictionary<string, object?>> Query(string query, string scope = @"root\cimv2")
    {
        var rows = new List<Dictionary<string, object?>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, query);
            searcher.Options.Timeout = TimeSpan.FromSeconds(10);
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in obj.Properties)
                        row[p.Name] = p.Value;
                    rows.Add(row);
                }
            }
        }
        catch (ManagementException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }

        return rows;
    }

    public static string Str(this Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) && v is not null ? v.ToString() ?? "" : "";

    public static long? Long(this Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var v) || v is null)
            return null;
        try
        {
            return Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    public static DateTime? CimDate(this Dictionary<string, object?> row, string key)
    {
        var s = row.Str(key);
        if (s.Length < 8)
            return null;
        try
        {
            return ManagementDateTimeConverter.ToDateTime(s);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
