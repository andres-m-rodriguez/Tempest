# Case study: Harmony MiniPlayer — today (beta.5)

The component that best shows where Tempest.WinUI needs more sugar. It *is* a
`StatefulStore`, but almost nothing in it uses what the library generates — every
missing feature has been hand-rolled around it:

- **Five commands with zero `[Command]`** — each one an identical
  `try { if (session is not null) await session.Try…(); } catch { }`, which is
  `TryExecute` reimplemented minus the `Error` capture. They aren't `[Command]`s
  because XAML buttons need `ICommand` (or a `Click` handler) — so the control adds
  five `async void On*Click` forwarders on top.
- **The marshaling tax** — every SMTC callback arrives off-thread, so every wire is a
  double-wrap: `dispatcher.TryEnqueue(() => _ = DispatchEvent(…))`, four times, plus
  the VM fetching its own `DispatcherQueue`.
- **`DispatchEvent` abused as `setState()`** — the class doc says it openly: events
  are "wrapped in DispatchEvent so the store broadcasts each batch of changes."
  A protected member meant for the generated registration code became the manual
  mutate-and-notify primitive, even for `SeekTo`'s three property writes.
- **Hand-maintained enablement** — `CanPrevious`/`CanNext`/`CanSeek`/`CanShuffle`/
  `CanRepeat` properties, each bound to a button's `IsEnabled` in markup.

## `Features/Music/ViewModels/MiniPlayerViewModel.cs`

```csharp
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Tempest;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Harmony.Desktop.Features.Music.ViewModels;

/// <summary>
/// Transport bar over the GSMTC session MediaTracker picks: art, seek, play/pause,
/// previous/next, shuffle and repeat — all local to the machine, no Spotify API.
/// Session events arrive on worker threads and are marshalled to the dispatcher, then
/// wrapped in DispatchEvent so the store broadcasts each batch of changes; the seek bar
/// extrapolates between the app's sparse timeline reports while playing.
/// Singleton: it outlives shell rebuilds across login cycles, like MainViewModel.
/// </summary>
public sealed partial class MiniPlayerViewModel : StatefulStore
{
    private readonly DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer ticker;
    private readonly DispatcherQueueTimer seekDebounce;

    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> mediaChanged;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> playbackChanged;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> timelineChanged;

    private GlobalSystemMediaTransportControlsSession? session;
    private MediaPlaybackAutoRepeatMode repeatMode;
    private TimeSpan basePosition;
    private DateTimeOffset baseTimestamp;
    private double playbackRate = 1;
    private double pendingSeekSeconds;
    private DateTimeOffset holdThumbUntil = DateTimeOffset.MinValue;

    public MiniPlayerViewModel(IEventBus bus, MediaTracker mediaTracker) : base(bus)
    {
        ticker = dispatcher.CreateTimer();
        ticker.Interval = TimeSpan.FromMilliseconds(500);
        ticker.Tick += (_, _) => _ = DispatchEvent(UpdatePosition);

        // Drags fire a burst of value changes; only the resting position is sent.
        seekDebounce = dispatcher.CreateTimer();
        seekDebounce.Interval = TimeSpan.FromMilliseconds(250);
        seekDebounce.IsRepeating = false;
        seekDebounce.Tick += (_, _) => _ = TrySeek();

        mediaChanged = (_, _) => dispatcher.TryEnqueue(() => _ = DispatchEvent(RefreshMedia));
        playbackChanged = (_, _) => dispatcher.TryEnqueue(() => _ = DispatchEvent(RefreshPlayback));
        timelineChanged = (_, _) => dispatcher.TryEnqueue(() => _ = DispatchEvent(RefreshTimeline));
        mediaTracker.SessionChanged += next => dispatcher.TryEnqueue(() => _ = DispatchEvent(() => Attach(next)));
    }

    public bool HasSession { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Artist { get; private set; } = string.Empty;

    public ImageSource? Art { get; private set; }

    public bool IsPlaying { get; private set; }

    public string PlayPauseGlyph => IsPlaying ? "" : "";

    public double PositionSeconds { get; private set; }

    public double DurationSeconds { get; private set; } = 1;

    public string PositionText { get; private set; } = "0:00";

    public string DurationText { get; private set; } = "0:00";

    public bool IsShuffle { get; private set; }

    public bool IsRepeatActive { get; private set; }

    public string RepeatGlyph { get; private set; } = "";

    public bool CanPrevious { get; private set; }

    public bool CanNext { get; private set; }

    public bool CanSeek { get; private set; }

    public bool CanShuffle { get; private set; }

    public bool CanRepeat { get; private set; }

    public async Task TogglePlayPause()
    {
        try
        {
            if (session is not null)
                await session.TryTogglePlayPauseAsync();
        }
        catch (Exception)
        {
        }
    }

    public async Task Previous()
    {
        try
        {
            if (session is not null)
                await session.TrySkipPreviousAsync();
        }
        catch (Exception)
        {
        }
    }

    public async Task Next()
    {
        try
        {
            if (session is not null)
                await session.TrySkipNextAsync();
        }
        catch (Exception)
        {
        }
    }

    public async Task ToggleShuffle()
    {
        try
        {
            if (session is not null)
                await session.TryChangeShuffleActiveAsync(!IsShuffle);
        }
        catch (Exception)
        {
        }
    }

    public async Task CycleRepeat()
    {
        try
        {
            if (session is not null)
                await session.TryChangeAutoRepeatModeAsync(repeatMode switch
                {
                    MediaPlaybackAutoRepeatMode.None => MediaPlaybackAutoRepeatMode.List,
                    MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track,
                    _ => MediaPlaybackAutoRepeatMode.None,
                });
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Called by the slider on user movement; the thumb holds while the seek lands.</summary>
    public void SeekTo(double seconds)
    {
        if (session is null || !CanSeek)
            return;

        _ = DispatchEvent(() =>
        {
            pendingSeekSeconds = seconds;
            holdThumbUntil = DateTimeOffset.UtcNow.AddSeconds(1.5);
            PositionSeconds = seconds;
            PositionText = Format(TimeSpan.FromSeconds(seconds));
        });
        seekDebounce.Stop();
        seekDebounce.Start();
    }

    private async Task TrySeek()
    {
        try
        {
            if (session is not null)
                await session.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(pendingSeekSeconds).Ticks);
        }
        catch (Exception)
        {
        }
    }

    private async Task Attach(GlobalSystemMediaTransportControlsSession? next)
    {
        if (ReferenceEquals(session, next))
            return;

        if (session is not null)
        {
            session.MediaPropertiesChanged -= mediaChanged;
            session.PlaybackInfoChanged -= playbackChanged;
            session.TimelinePropertiesChanged -= timelineChanged;
        }

        session = next;
        HasSession = session is not null;
        if (session is null)
        {
            ticker.Stop();
            Art = null;
            return;
        }

        session.MediaPropertiesChanged += mediaChanged;
        session.PlaybackInfoChanged += playbackChanged;
        session.TimelinePropertiesChanged += timelineChanged;
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
            RepeatGlyph = repeatMode == MediaPlaybackAutoRepeatMode.Track ? "" : "";

            var controls = info.Controls;
            CanPrevious = controls.IsPreviousEnabled;
            CanNext = controls.IsNextEnabled;
            CanSeek = controls.IsPlaybackPositionEnabled;
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

## `Features/Music/Controls/MiniPlayer.xaml.cs`

```csharp
using Harmony.Desktop.Features.Music.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Harmony.Desktop.Features.Music.Controls;

/// <summary>Transport bar for the picked media session; hides itself when there is none.</summary>
public sealed partial class MiniPlayer : UserControl
{
    public MiniPlayer()
    {
        Vm = App.Services.GetRequiredService<MiniPlayerViewModel>();
        InitializeComponent();
    }

    public MiniPlayerViewModel Vm { get; }

    public Brush ActiveBrush(bool active) =>
        (Brush)Application.Current.Resources[active ? "AccentSpringBrush" : "TextSecondaryBrush"];

    private void OnSeekBarValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // The binding echoes the VM position exactly; anything else is the user's thumb.
        if (Math.Abs(e.NewValue - Vm.PositionSeconds) > 0.5)
            Vm.SeekTo(e.NewValue);
    }

    private async void OnToggleShuffleClick(object sender, RoutedEventArgs e) => await Vm.ToggleShuffle();

    private async void OnPreviousClick(object sender, RoutedEventArgs e) => await Vm.Previous();

    private async void OnTogglePlayPauseClick(object sender, RoutedEventArgs e) => await Vm.TogglePlayPause();

    private async void OnNextClick(object sender, RoutedEventArgs e) => await Vm.Next();

    private async void OnCycleRepeatClick(object sender, RoutedEventArgs e) => await Vm.CycleRepeat();
}
```

## `Features/Music/Controls/MiniPlayer.xaml` (transport section)

```xml
<StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="2" VerticalAlignment="Center">
    <Button
        Style="{StaticResource TransportButton}"
        Click="OnToggleShuffleClick"
        IsEnabled="{x:Bind Vm.CanShuffle, Mode=OneWay}"
        ToolTipService.ToolTip="Shuffle">
        <FontIcon Glyph="&#xE8B1;" FontSize="14" Foreground="{x:Bind ActiveBrush(Vm.IsShuffle), Mode=OneWay}" />
    </Button>
    <Button
        Style="{StaticResource TransportButton}"
        Click="OnPreviousClick"
        IsEnabled="{x:Bind Vm.CanPrevious, Mode=OneWay}"
        ToolTipService.ToolTip="Previous">
        <FontIcon Glyph="&#xE892;" FontSize="14" />
    </Button>
    <Button
        Style="{StaticResource TransportButton}"
        Click="OnTogglePlayPauseClick"
        ToolTipService.ToolTip="Play/Pause">
        <FontIcon Glyph="{x:Bind Vm.PlayPauseGlyph, Mode=OneWay}" FontSize="20" />
    </Button>
    <Button
        Style="{StaticResource TransportButton}"
        Click="OnNextClick"
        IsEnabled="{x:Bind Vm.CanNext, Mode=OneWay}"
        ToolTipService.ToolTip="Next">
        <FontIcon Glyph="&#xE893;" FontSize="14" />
    </Button>
    <Button
        Style="{StaticResource TransportButton}"
        Click="OnCycleRepeatClick"
        IsEnabled="{x:Bind Vm.CanRepeat, Mode=OneWay}"
        ToolTipService.ToolTip="Repeat">
        <FontIcon Glyph="{x:Bind Vm.RepeatGlyph, Mode=OneWay}" FontSize="14" Foreground="{x:Bind ActiveBrush(Vm.IsRepeatActive), Mode=OneWay}" />
    </Button>
</StackPanel>

<Slider
    Grid.Column="4"
    Value="{x:Bind Vm.PositionSeconds, Mode=OneWay}"
    IsEnabled="{x:Bind Vm.CanSeek, Mode=OneWay}"
    ValueChanged="OnSeekBarValueChanged"
    StepFrequency="1" ... />
```
