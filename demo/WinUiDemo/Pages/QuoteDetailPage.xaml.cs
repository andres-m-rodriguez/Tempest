using Microsoft.UI.Xaml.Navigation;
using Tempest;
using WinUiDemo.Services;

namespace WinUiDemo.Pages;

// The navigation-parameter pattern: OnNavigatedTo fires before Loaded, so state
// seeded here is already in place when the [RunOnLoad] command fires — no handoff
// ritual between navigation and loading. Services still arrive through the primary
// constructor.
public sealed partial class QuoteDetailPage(QuoteService quotes) : StatefulPage
{
    [Reactive] private string _quote = "";

    public string AnalysisText { get; private set; } = "Analyzing…";

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _quote = (string)e.Parameter;
    }

    [Command, RunOnLoad]
    private async Task Analyze(CancellationToken ct)
        => AnalysisText = await quotes.Analyze(_quote, ct);
}
