namespace Stockpile.Application.Common.Querying;

/// <summary>
/// Turns what someone typed into a search box into a SQL LIKE pattern that means it.
/// <para>
/// Interpolating the term straight into '%...%' hands the user's own characters to the
/// pattern matcher: a search for "50%" matches everything starting "50", "A_B" matches
/// "AxB", and a lone "%" returns the entire table one page at a time. None of those is a
/// search — they are the pattern language leaking through the box.
/// </para>
/// </summary>
public static class LikePattern
{
    /// <summary>
    /// Named in the ESCAPE clause. A single backslash; it is also PostgreSQL's default, so
    /// the two agree either way. Verbatim strings throughout — a pattern about escaping is
    /// the worst place to also be counting C# escapes.
    /// </summary>
    public const string EscapeCharacter = @"\";

    /// <summary>A contains-match for <paramref name="term"/>, taken literally.</summary>
    public static string Contains(string term) => $"%{Escape(term)}%";

    // The escape character goes first, or it escapes the escapes added after it.
    private static string Escape(string term) => term
        .Replace(@"\", @"\\")
        .Replace("%", @"\%")
        .Replace("_", @"\_");
}
