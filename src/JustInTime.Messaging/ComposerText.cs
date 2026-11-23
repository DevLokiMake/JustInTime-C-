namespace JustInTime.Messaging;

/// <summary>
/// Сравнение текста в поле ввода с тем, что мы вводили. Веб-редакторы и Qt по-разному возвращают переводы строк
/// (\n, \r\n, \r) и добавляют невидимые символы, поэтому перед сравнением всё приводится к одному виду.
/// </summary>
internal static class ComposerText
{
    public static string Canonical(string value) =>
        new string(value
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Where(c => c is not ('\u200B' or '\u200C' or '\u200D' or '\uFEFF' or '\u00A0'))
                .ToArray())
            .Trim();

    public static bool Same(string actual, string expected) =>
        string.Equals(Canonical(actual), Canonical(expected), StringComparison.Ordinal);
}
