using System.ComponentModel;
using Microsoft.Win32;

namespace PBLanHelper;

public sealed class Entry : INotifyPropertyChanged
{
    bool _enabled = true;
    string _value = "";
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }
}

public static class AppSettings
{
    const string KeyPath = @"Software\PBLanHelper";

    public const string AppDataFolder = "Zepetto";
    public const string LauncherRelPath = @"PointBlank\PBLauncher.exe";
    public static readonly string[] ProcessNames = { "PointBlank", "PBLauncher" };

    public static string Destination
    {
        get => GetString(nameof(Destination), @"C:\Zepetto");
        set => SetString(nameof(Destination), value);
    }

    // Origens SMB, em ordem de prioridade (migra Primary/Secondary da v1.0.0)
    public static List<Entry> LoadSmbs() =>
        ReadList("Smbs") ?? new List<Entry>
        {
            new() { Value = GetString("Primary", @"z:\master") },
            new() { Value = GetString("Secondary", @"y:\backup") },
        };

    // Arquivos .zip (caminho relativo dentro de cada origem)
    public static List<Entry> LoadZips() =>
        ReadList("Zips") ?? new List<Entry>
        {
            new() { Value = "LIVE.zip" },
            new() { Value = "QA.zip" },
        };

    public static void SaveSmbs(IEnumerable<Entry> list) => WriteList("Smbs", list);
    public static void SaveZips(IEnumerable<Entry> list) => WriteList("Zips", list);

    static List<Entry>? ReadList(string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
        if (k?.GetValue(name) is not string[] lines) return null;
        return lines.Where(s => s.Length > 2 && s[1] == '|')
                    .Select(s => new Entry { Enabled = s[0] == '1', Value = s[2..] })
                    .ToList();
    }

    static void WriteList(string name, IEnumerable<Entry> list)
    {
        var lines = list.Where(e => !string.IsNullOrWhiteSpace(e.Value))
                        .Select(e => (e.Enabled ? "1|" : "0|") + e.Value.Trim())
                        .ToArray();
        using var k = Registry.CurrentUser.CreateSubKey(KeyPath);
        k.SetValue(name, lines, RegistryValueKind.MultiString);
    }

    static string GetString(string name, string def)
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
        return k?.GetValue(name) as string ?? def;
    }

    static void SetString(string name, string value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(KeyPath);
        k.SetValue(name, value);
    }
}