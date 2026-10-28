namespace XivType;

// Window text in English, Japanese or Traditional Chinese. Auto follows the game client language.
internal static class Loc
{
    internal static Configuration? Config { get; set; }
    internal static readonly string[] LanguageLabels = ["Auto", "English", "日本語", "繁體中文"];

    private static int Language => Config?.UiLanguage is >= 1 and <= 3 and var chosen ? chosen
        : Plugin.Client.ClientLanguage.ToString() == "Japanese" ? 2 : 1;

    internal static string T(string english, string japanese, string chinese) => Language switch
    {
        2 => japanese,
        3 => chinese,
        _ => english,
    };
}
