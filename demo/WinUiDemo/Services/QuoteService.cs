namespace WinUiDemo.Services;

/// <summary>Stands in for a real API client: registered in App.OnLaunched and
/// resolved by QuotePage's generated constructor bridge — never newed up by a page.</summary>
public sealed class QuoteService
{
    private static readonly string[] Quotes =
    [
        "Simplicity is prerequisite for reliability. — Edsger Dijkstra",
        "Make it work, make it right, make it fast. — Kent Beck",
        "Programs must be written for people to read. — Harold Abelson",
        "The best way to predict the future is to invent it. — Alan Kay",
    ];

    private int _next;

    public async Task<string> GetQuote(CancellationToken ct)
    {
        await Task.Delay(600, ct);
        return Quotes[_next++ % Quotes.Length];
    }

    public async Task<string> Analyze(string quote, CancellationToken ct)
    {
        await Task.Delay(400, ct);
        var words = quote.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return $"{words} words, {quote.Length} characters.";
    }
}
