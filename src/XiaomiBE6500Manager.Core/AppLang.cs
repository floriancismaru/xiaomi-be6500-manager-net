namespace XiaomiBE6500Manager.Core;

/// <summary>Languages the app can show the router UI (and its own screens) in.</summary>
public sealed record AppLang(string Code, string Label)
{
    public static readonly AppLang En = new("en", "English");
    public static readonly AppLang Ro = new("ro", "Română");
    public static readonly IReadOnlyList<AppLang> All = [En, Ro];

    public static AppLang ByCode(string? code) => All.FirstOrDefault(l => l.Code == code) ?? En;

    public override string ToString() => Label;
}

/// <summary>App UI strings (English / Romanian).</summary>
public static class Strings
{
    static readonly Dictionary<string, Dictionary<string, string>> S = new()
    {
        ["en"] = new()
        {
            ["title"] = "Xiaomi BE6500 Manager",
            ["routerIp"] = "Router IP address",
            ["language"] = "Language",
            ["connect"] = "Connect",
            ["invalidIp"] = "Enter a valid private IPv4 address",
            ["unreachable"] = "Router not reachable at this address",
            ["connecting"] = "Connecting…",
            ["untranslated"] = "Untranslated: {0}",
            ["untranslatedTip"] = "{0} untranslated strings recorded in {1}",
            ["home"] = "Home",
            ["back"] = "Back",
            ["forward"] = "Forward",
            ["reload"] = "Reload",
            ["original"] = "中文 (original)",
        },
        ["ro"] = new()
        {
            ["title"] = "Xiaomi BE6500 Manager",
            ["routerIp"] = "Adresa IP a routerului",
            ["language"] = "Limba",
            ["connect"] = "Conectare",
            ["invalidIp"] = "Introduceți o adresă IPv4 privată validă",
            ["unreachable"] = "Routerul nu poate fi accesat la această adresă",
            ["connecting"] = "Se conectează…",
            ["untranslated"] = "Netraduse: {0}",
            ["untranslatedTip"] = "{0} șiruri netraduse salvate în {1}",
            ["home"] = "Acasă",
            ["back"] = "Înapoi",
            ["forward"] = "Înainte",
            ["reload"] = "Reîncarcă",
            ["original"] = "中文 (original)",
        },
    };

    public static string Tr(AppLang lang, string key) =>
        S[lang.Code].TryGetValue(key, out var v) ? v : key;
}
