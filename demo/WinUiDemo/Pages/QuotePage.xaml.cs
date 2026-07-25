using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tempest;
using WinUiDemo.Services;

namespace WinUiDemo.Pages;

// A full Tempest component on the Page host: [Reactive] + [OnChanged], [Command] +
// [CanExecute], [Event], and [RunOnLoad] — everything the control demo shows, plus
// the two page-only tricks: no constructor anywhere in this file (the primary
// constructor declares what the page needs; the generated twin adds the parameterless
// bridge Frame.Navigate calls, resolving QuoteService from the provider App passed to
// UseServices, then calling InitializeComponent), and [RunOnLoad] replacing the
// Loaded-handler ritual.
public sealed partial class QuotePage(QuoteService quotes) : StatefulPage
{
    [Reactive] private string _notes = "";

    public string QuoteText { get; private set; } = "";

    public string LoadLabel => LoadState.IsLoading ? "Loading…" : "Another one";

    public string NotesSummary { get; private set; } = "No notes yet";

    [OnChanged]
    private void OnNotesChanged(string value)
        => NotesSummary = value.Length == 0 ? "No notes yet" : $"{value.Length} characters of notes";

    [Command, RunOnLoad]
    private async Task Load(CancellationToken ct)
        => QuoteText = await quotes.GetQuote(ct);

    [Command]
    private void ClearNotes() => NotesState.Reset();

    // Gates the ClearNotes button: re-evaluated on every state broadcast, so the
    // button enables the moment the first character lands.
    [CanExecute]
    private bool OnClearNotesCanExecute() => NotesState.Value.Length > 0;

    public sealed record QuoteRequested;

    // The doorbell: anyone with the bus can ask the page for a fresh quote —
    // MainWindow publishes this from outside the page.
    [Event]
    private Task OnQuoteRequested(QuoteRequested e) => LoadState.TryExecute();

    private void NotesChanged(object sender, TextChangedEventArgs e)
        => NotesState.Value = ((TextBox)sender).Text;

    // Page-to-page navigation stays the ordinary Frame ritual — the parameter lands
    // in the detail page's OnNavigatedTo.
    private void DetailsClicked(object sender, RoutedEventArgs e)
        => Frame.Navigate(typeof(QuoteDetailPage), QuoteText);

    private void RingDoorbellClicked(object sender, RoutedEventArgs e)
        => Bus.Publish(new QuoteRequested());
}
