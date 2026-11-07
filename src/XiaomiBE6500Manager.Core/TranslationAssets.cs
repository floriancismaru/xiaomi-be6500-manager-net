using System.Reflection;

namespace XiaomiBE6500Manager.Core;

/// <summary>The dictionary and in-page translator bundled with the app.</summary>
public static class TranslationAssets
{
    public static string Dictionary => Read("dictionary.json");
    public static string Translator => Read("translator.js");

    static string Read(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new FileNotFoundException(name);
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
