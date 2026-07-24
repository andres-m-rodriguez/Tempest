# Case study: Harmony MiniPlayer — after beta.6

The same component, rewritten the Tempest way: **no ViewModel, and no raw event
plumbing**. The MVVM split existed because XAML had nothing better — with Tempest,
the code-behind *is* the component, exactly like a Blazor page's `@code` block.
`MiniPlayerViewModel.cs` is deleted; everything lives in
`MiniPlayer : StatefulControl`, and `x:Bind` binds straight at the control's own
members — which is `x:Bind`'s natural default anyway, so every `Vm.` prefix
disappears from markup too.

The second deletion is the event wiring. The old component stored three
`TypedEventHandler` fields and hand-managed `+=`/`-=` across session switches —
MVVM-era ceremony. Tempest already has a shape for "the outside world reaching in":
**`[Event]` doorbells**. The raw WinRT subscriptions move into `MediaTracker` — the
GSMTC infrastructure service that already owns session picking — which republishes
them as bus events. The component just declares handlers. This is also the cleaner
threading story: bus handlers are dispatched through the host's `DispatchEvent`,
which already marshals to the UI thread — no per-callback marshaling in user code at
all.

**Features this rewrite assumes (proposed for beta.6):**

1. **`CommandStateBase : ICommand`** (`Tempest.Abstract`) — `Execute(object?)` routes
   through the fire-safe `TryExecute`, `CanExecute` is `!IsLoading && predicate`, and
   `CanExecuteChanged` is raised on every re-render broadcast, so buttons enable and
   disable themselves.
2. **`[CanExecute]`** (`Tempest.Abstract` + pipeline) — the `[OnChanged]` pattern
   applied to command enablement, with the same naming grammar: mark a bool member;
   bare, its `On{Command}CanExecute` name picks the command (`OnNextCanExecute` →
   `Next`); the constructor argument targets one explicitly
   (`[CanExecute(nameof(ToggleShuffle))]`), freeing the member name. Resolved in the
   compiler exactly like hooks — unmatched and duplicate candidates get their own TEM
   diagnostics — and the generated state uses the member as its
   `ICommand.CanExecute` predicate.
3. **`[RunOnLoad]`** (`Tempest.Abstract` + pipeline) — stacked on a `[Command]` like
   `[Event, Command]` stacks roles: the generated registration runs the command when
   the host initializes (Blazor `OnInitialized`, XAML `Loaded`, a store's
   construction). It replaces the hand-written `Loaded += … TryExecute` ritual here
   — and the identical `OnInitializedAsync() => await LoadState.TryExecute();`
   one-liner every Blazor page writes today. Here it seeds the current session on
   load, since the bus only carries *changes*.
4. **`Mutate(Action)`** on the host bases — the blessed mutate-and-notify primitive
   `DispatchEvent` was standing in for: run a batch of property writes, broadcast
   once. After the event wiring moved to the bus, only the ticker and the seek
   debounce still need it.

## `Features/Music/Controls/MiniPlayer.xaml.cs` — the whole component

```csharp
using Harmony.Desktop.Features.Music.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Tempest;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Harmony.Desktop.Features.Music.Controls;

/// <summary>
/// Transport bar over the GSMTC session MediaTracker picks: art, seek, play/pause,
/// previous/next, shuffle and repeat — all local to the machine, no Spotify API.
/// MediaTracker owns the raw WinRT session events and rings this component's
/// doorbells on the bus; handlers arrive already marshalled to the UI thread. The
/// seek bar extrapolates between the app's sparse timeline reports while playing.
/// Hides itself when there is no session.
/// </summary>
public sealed partial class MiniPlayer : StatefulControl
{
    private readonly MediaTracker mediaTracker = App.Services.GetRequiredService<MediaTracker>();
    private readonly DispatcherQueueTimer ticker;
    private readonly DispatcherQueueTimer seekDebounce;

    private GlobalSystemMediaTransportControlsSession? session;
    private MediaPlaybackAutoRepeatMode repeatMode;
    private TimeSpan basePosition;
    private DateTimeOffset baseTimestamp;
    private double playbackRate = 1;
    private double pendingSeekSeconds;
    private DateTimeOffset holdThumbUntil = DateTimeOffset.MinValue;

    public MiniPlayer()
    {
        InitializeComponent();

        ticker = DispatcherQueue.CreateTimer();
        ticker.Interval = TimeSpan.FromMilliseconds(500);
        ticker.Tick += (_, _) => _ = Mutate(UpdatePosition);

        // Drags fire a burst of value changes; only the resting position is sent.
        seekDebounce = DispatcherQueue.CreateTimer();
        seekDebounce.Interval = TimeSpan.FromMilliseconds(250);
        seekDebounce.IsRepeating = false;
        seekDebounce.Tick += (_, _) => _ = SeekState.TryExecute();
    }

    [CanExecute(nameof(PlayPause))]
    public bool HasSession { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Artist { get; private set; } = string.Empty;

    public ImageSource? Art { get; private set; }

    public bool IsPlaying { get; private set; }

    public string PlayPauseGlyph => IsPlaying ? "" : "";

    public double PositionSeconds { get; private set; }

    public double DurationSeconds { get; private set; } = 1;

    public string PositionText { get; private set; } = "0:00";

    public string DurationText { get; private set; } = "0:00";

    public bool IsShuffle { get; private set; }

    public bool IsRepeatActive { get; private set; }

    public string RepeatGlyph { get; private set; } = "";

    // Bare [CanExecute] resolves by the On{Command}CanExecute name convention — the
    // same grammar as a bare [OnChanged]; the targeted form frees the member name
    // when the command is called something else.

    [CanExecute]
    public bool OnPreviousCanExecute { get; private set; }

    [CanExecute]
    public bool OnNextCanExecute { get; private set; }

    [CanExecute]
    public bool OnSeekCanExecute { get; private set; }

    [CanExecute(nameof(ToggleShuffle))]
    public bool CanShuffle { get; private set; }

    [CanExecute(nameof(CycleRepeat))]
    public bool CanRepeat { get; private set; }

    public Brush ActiveBrush(bool active) =>
        (Brush)Application.Current.Resources[active ? "AccentSpringBrush" : "TextSecondaryBrush"];

    // ── The outside world reaching in ────────────────────────────────────────
    // The records are the contract (SPEC: "anyone publishes it"); MediaTracker owns
    // the raw WinRT subscriptions and publishes these on the bus. Handlers run
    // through the host's DispatchEvent, so they arrive on the UI thread with a
    // broadcast after each — no marshaling, no stored handler fields, no +=/-=.

    public sealed record SessionSwitched(GlobalSystemMediaTransportControlsSession? Session);
    public sealed record MediaChanged;
    public sealed record PlaybackChanged;
    public sealed record TimelineChanged;

    [Event]
    private async Task OnSessionSwitched(SessionSwitched e) => await Attach(e.Session);

    [Event]
    private async Task OnMediaChanged(MediaChanged e) => await RefreshMedia();

    [Event]
    private void OnPlaybackChanged(PlaybackChanged e) => RefreshPlayback();

    [Event]
    private void OnTimelineChanged(TimelineChanged e) => RefreshTimeline();

    // The bus only carries changes; seed the current session when the control loads.
    [Command, RunOnLoad]
    private Task Load(CancellationToken ct) => Attach(mediaTracker.Current);

    // ── The five transport commands ──────────────────────────────────────────
    // TryExecute replaces the try/catch bodies (failures land in {Name}State.Error
    // instead of an empty catch), and the [CanExecute] members above replace both
    // the session null-checks and the IsEnabled bindings.

    [Command]
    private async Task PlayPause(CancellationToken ct) => await session!.TryTogglePlayPauseAsync();

    [Command]
    private async Task Previous(CancellationToken ct) => await session!.TrySkipPreviousAsync();

    [Command]
    private async Task Next(CancellationToken ct) => await session!.TrySkipNextAsync();

    [Command]
    private async Task ToggleShuffle(CancellationToken ct) =>
        await session!.TryChangeShuffleActiveAsync(!IsShuffle);

    [Command]
    private async Task CycleRepeat(CancellationToken ct) =>
        await session!.TryChangeAutoRepeatModeAsync(repeatMode switch
        {
            MediaPlaybackAutoRepeatMode.None => MediaPlaybackAutoRepeatMode.List,
            MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track,
            _ => MediaPlaybackAutoRepeatMode.None,
        });

    [Command]
    private async Task Seek(CancellationToken ct) =>
        await session!.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(pendingSeekSeconds).Ticks);

    private void OnSeekBarValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // The binding echoes the position exactly; anything else is the user's thumb.
        if (Math.Abs(e.NewValue - PositionSeconds) <= 0.5 || !OnSeekCanExecute)
            return;

        var seconds = e.NewValue;
        _ = Mutate(() =>
        {
            pendingSeekSeconds = seconds;
            holdThumbUntil = DateTimeOffset.UtcNow.AddSeconds(1.5);
            PositionSeconds = seconds;
            PositionText = Format(TimeSpan.FromSeconds(seconds));
        });
        seekDebounce.Stop();
        seekDebounce.Start();
    }

    private async Task Attach(GlobalSystemMediaTransportControlsSession? next)
    {
        if (ReferenceEquals(session, next))
            return;

        session = next;
        HasSession = session is not null;
        if (session is null)
        {
            ticker.Stop();
            Art = null;
            return;
        }

        RefreshPlayback();
        RefreshTimeline();
        await RefreshMedia();
    }

    private async Task RefreshMedia()
    {
        if (session is null)
            return;

        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            Title = properties?.Title ?? string.Empty;
            Artist = properties?.Artist ?? string.Empty;

            // The source app hands over the actual cover art — no CDN, no API.
            if (properties?.Thumbnail is IRandomAccessStreamReference thumbnail)
            {
                using var stream = await thumbnail.OpenReadAsync();
                var image = new BitmapImage();
                await image.SetSourceAsync(stream);
                Art = image;
            }
            else
            {
                Art = null;
            }
        }
        catch (Exception)
        {
            // A dying session can fail mid-read; the next change recovers.
        }
    }

    private void RefreshPlayback()
    {
        if (session is null)
            return;

        try
        {
            var info = session.GetPlaybackInfo();
            IsPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            playbackRate = info.PlaybackRate ?? 1;
            IsShuffle = info.IsShuffleActive ?? false;
            repeatMode = info.AutoRepeatMode ?? MediaPlaybackAutoRepeatMode.None;
            IsRepeatActive = repeatMode != MediaPlaybackAutoRepeatMode.None;
            RepeatGlyph = repeatMode == MediaPlaybackAutoRepeatMode.Track ? "" : "";

            var controls = info.Controls;
            OnPreviousCanExecute = controls.IsPreviousEnabled;
            OnNextCanExecute = controls.IsNextEnabled;
            OnSeekCanExecute = controls.IsPlaybackPositionEnabled;
            CanShuffle = controls.IsShuffleEnabled;
            CanRepeat = controls.IsRepeatEnabled;

            if (IsPlaying)
                ticker.Start();
            else
                ticker.Stop();

            RefreshTimeline();
        }
        catch (Exception)
        {
        }
    }

    private void RefreshTimeline()
    {
        if (session is null)
            return;

        try
        {
            var timeline = session.GetTimelineProperties();
            basePosition = timeline.Position;
            baseTimestamp = timeline.LastUpdatedTime;

            var duration = timeline.EndTime - timeline.StartTime;
            DurationSeconds = Math.Max(duration.TotalSeconds, 1);
            DurationText = Format(duration);
            UpdatePosition();
        }
        catch (Exception)
        {
        }
    }

    private void UpdatePosition()
    {
        if (DateTimeOffset.UtcNow < holdThumbUntil)
            return;

        var position = IsPlaying
            ? basePosition + (DateTimeOffset.UtcNow - baseTimestamp) * playbackRate
            : basePosition;

        if (position < TimeSpan.Zero)
            position = TimeSpan.Zero;
        var duration = TimeSpan.FromSeconds(DurationSeconds);
        if (position > duration)
            position = duration;

        PositionSeconds = position.TotalSeconds;
        PositionText = Format(position);
    }

    private static string Format(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
}
```

## `MediaTracker` — where the WinRT plumbing now lives

The tracker already owns session picking; it gains the per-session subscriptions it
should always have owned, and translates them into doorbell publishes. Publishing a
component's nested record from a service is the documented pattern — the record is
the contract, "anyone publishes it."

```csharp
// Inside MediaTracker, on session switch:
private void OnSessionPicked(GlobalSystemMediaTransportControlsSession? next)
{
    if (current is not null)
    {
        current.MediaPropertiesChanged -= mediaChanged;
        current.PlaybackInfoChanged -= playbackChanged;
        current.TimelinePropertiesChanged -= timelineChanged;
    }

    current = next;
    if (current is not null)
    {
        current.MediaPropertiesChanged += mediaChanged;      // = (_, _) => bus.Publish(new MiniPlayer.MediaChanged());
        current.PlaybackInfoChanged += playbackChanged;      // = (_, _) => bus.Publish(new MiniPlayer.PlaybackChanged());
        current.TimelinePropertiesChanged += timelineChanged; // = (_, _) => bus.Publish(new MiniPlayer.TimelineChanged());
    }

    bus.Publish(new MiniPlayer.SessionSwitched(next));
}

public GlobalSystemMediaTransportControlsSession? Current => current;
```

Publishing from a worker thread is safe: the bus is fire-and-forget guarded, and each
subscriber's handler runs through its host's `DispatchEvent` — the UI-thread marshal
is the host's job, done once, in the library.

## `Features/Music/Controls/MiniPlayer.xaml` (transport section)

The root element is `tempest:StatefulControl`; every binding drops its `Vm.` prefix
because `x:Bind` binds at the control itself — the same "markup over my own members"
relationship a `.razor` file has with its `@code` block.

```xml
<StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="2" VerticalAlignment="Center">
    <Button
        Style="{StaticResource TransportButton}"
        Command="{x:Bind ToggleShuffleState}"
        ToolTipService.ToolTip="Shuffle">
        <FontIcon Glyph="&#xE8B1;" FontSize="14" Foreground="{x:Bind ActiveBrush(IsShuffle), Mode=OneWay}" />
    </Button>
    <Button
        Style="{StaticResource TransportButton}"
        Command="{x:Bind PreviousState}"
        ToolTipService.ToolTip="Previous">
        <FontIcon Glyph="&#xE892;" FontSize="14" />
    </Button>
    <Button
        Style="{StaticResource TransportButton}"
        Command="{x:Bind PlayPauseState}"
        ToolTipService.ToolTip="Play/Pause">
        <FontIcon Glyph="{x:Bind PlayPauseGlyph, Mode=OneWay}" FontSize="20" />
    </Button>
    <Button
        Style="{StaticResource TransportButton}"
        Command="{x:Bind NextState}"
        ToolTipService.ToolTip="Next">
        <FontIcon Glyph="&#xE893;" FontSize="14" />
    </Button>
    <Button
        Style="{StaticResource TransportButton}"
        Command="{x:Bind CycleRepeatState}"
        ToolTipService.ToolTip="Repeat">
        <FontIcon Glyph="{x:Bind RepeatGlyph, Mode=OneWay}" FontSize="14" Foreground="{x:Bind ActiveBrush(IsRepeatActive), Mode=OneWay}" />
    </Button>
</StackPanel>

<Slider
    Grid.Column="4"
    Value="{x:Bind PositionSeconds, Mode=OneWay}"
    IsEnabled="{x:Bind OnSeekCanExecute, Mode=OneWay}"
    ValueChanged="OnSeekBarValueChanged"
    StepFrequency="1" ... />
```

## What changed

| Before | After |
|---|---|
| Two classes: `MiniPlayerViewModel` (~300 lines) + `MiniPlayer` code-behind (~45) | **One class** — the code-behind is the component, like a `@code` block |
| `Vm = App.Services.GetRequiredService<MiniPlayerViewModel>()` + `Vm.` on every binding | Gone — `x:Bind` binds at the control's own members |
| 3 stored `TypedEventHandler` fields + `+=`/`-=` juggling in `Attach` | 4 `[Event]` doorbells; the WinRT subscriptions live in `MediaTracker` where infrastructure belongs |
| `dispatcher.TryEnqueue(() => _ = DispatchEvent(…))` × 4 + a `dispatcher` field | Nothing — bus handlers arrive through the host's `DispatchEvent`, already on the UI thread |
| 5 public async command methods with `try { if (session is not null) … } catch { }` | 5 `[Command]` one-liners; failures land in `{Name}State.Error` |
| `TrySeek` + its empty catch | `[Command] Seek`, fired by the debounce timer via `SeekState.TryExecute()` |
| 5 `async void On*Click` forwarders | `Command="{x:Bind …State}"` in markup |
| 4 `IsEnabled` bindings + per-command session null-checks | `[CanExecute]` members — bare by `Can{Command}` convention or explicitly targeted, like `[OnChanged]`; buttons self-disable |
| Hand-wired initial state | `[Command, RunOnLoad] Load` seeds from `MediaTracker.Current` |
| `DispatchEvent` repurposed as setState | `Mutate` for the two real batches left (ticker, seek thumb) |
| VM registered as an app singleton to survive shell rebuilds | State re-derives from the session on load; app-lifetime state stays in `MediaTracker`, where it belongs |

The genuinely domain-shaped code (the refresh readers with their dying-session
guards, timeline extrapolation, seek debounce) moved verbatim — none of it was
ceremony, and no library should try to absorb it. What a `StatefulStore` remains for
is *shared* state — used by more than one view or outliving all of them — not a
mandatory middleman for every screen.
