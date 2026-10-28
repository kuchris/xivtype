namespace XivType;

// Japanese client support (2026-10-29). Posted keystrokes bypass the IME, so they can never produce kana, and posted
// WM_CHAR / WM_IME_CHAR kana and IMM composition injection were ignored. A direct write of the kana scores, but a run
// with only a direct write and a posted Enter was expelled for inactivity within a minute, while posting the romaji as
// letter keys first and then overwriting them with the kana ran four minutes without expulsion. The romaji is only
// keyboard activity: the direct write replaces whatever it produced, so it does not need to be an exact IME spelling.
internal static class JapaneseInput
{
    private static readonly Dictionary<char, string> Romaji = Build();

    // The reading line is kana plus marks such as '～', '、' or '？' ('すま～いるをわすれるな', 2026-10-29); only kanji
    // and control characters rule a label out.
    internal static bool IsKana(string text) => text.Length > 0 && text.All(c => !char.IsControl(c) && c is not (>= '一' and <= '鿿'));

    internal static string ToRomaji(string kana)
    {
        var result = new System.Text.StringBuilder();
        foreach (var c in kana)
            result.Append(Romaji.TryGetValue(c, out var letters) ? letters : "a");
        return result.ToString();
    }

    private static Dictionary<char, string> Build()
    {
        var map = new Dictionary<char, string>();
        void Row(string kana, params string[] letters)
        {
            for (var i = 0; i < kana.Length; i++)
            {
                map[kana[i]] = letters[i];
                map[(char)(kana[i] + 0x60)] = letters[i]; // the katakana block mirrors hiragana 0x60 higher
            }
        }
        Row("あいうえお", "a", "i", "u", "e", "o");
        Row("ぁぃぅぇぉ", "a", "i", "u", "e", "o");
        Row("かきくけこ", "ka", "ki", "ku", "ke", "ko");
        Row("がぎぐげご", "ga", "gi", "gu", "ge", "go");
        Row("さしすせそ", "sa", "si", "su", "se", "so");
        Row("ざじずぜぞ", "za", "zi", "zu", "ze", "zo");
        Row("たちつてと", "ta", "ti", "tu", "te", "to");
        Row("だぢづでど", "da", "di", "du", "de", "do");
        Row("なにぬねの", "na", "ni", "nu", "ne", "no");
        Row("はひふへほ", "ha", "hi", "hu", "he", "ho");
        Row("ばびぶべぼ", "ba", "bi", "bu", "be", "bo");
        Row("ぱぴぷぺぽ", "pa", "pi", "pu", "pe", "po");
        Row("まみむめも", "ma", "mi", "mu", "me", "mo");
        Row("やゆよゃゅょ", "ya", "yu", "yo", "ya", "yu", "yo");
        Row("らりるれろ", "ra", "ri", "ru", "re", "ro");
        Row("わをんっ", "wa", "wo", "nn", "t");
        Row("ゔ", "vu");
        map['ー'] = "a";
        return map;
    }
}
