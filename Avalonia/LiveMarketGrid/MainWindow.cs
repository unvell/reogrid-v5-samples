using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using unvell.ReoGrid.Core;

namespace ReoGridSamples.LiveMarketGrid;

/// <summary>How the window asks the grid to repaint after a tick.</summary>
internal enum RepaintCall
{
	/// <summary><c>InvalidateVisual()</c> — repaint, nothing else. The call for "values changed".</summary>
	InvalidateVisual,

	/// <summary><c>RefreshExternal()</c> — also re-measures the scrollbars and raises SelectionChanged;
	/// meant for changes to the sheet's structure (rows, columns, sizes) made outside the control.</summary>
	RefreshExternal,
}

/// <summary>
/// A market-data monitor: three sheets of 100,000 instruments × 20 fields. The toolbar picks how
/// often the feed ticks, which cells a tick changes and how the repaint is requested; the status bar
/// shows what each repaint costs on the UI thread, how many reach the screen per second and what
/// writing the tick into the model cost.
/// </summary>
internal sealed class MainWindow : Window
{
	private static readonly (string Label, int Ms)[] Paces = [("Every frame", 0), ("50 ms", 50), ("100 ms", 100), ("250 ms", 250), ("1 s", 1000)];
	private static readonly (string Label, UpdatePattern Pattern)[] Patterns =
	[
		("Market feed (~85% of visible rows)", UpdatePattern.Market),
		("Clock column + 8 prices", UpdatePattern.ClockAndFew),
		("Every visible instrument", UpdatePattern.EveryVisible),
		("Nothing (repaint only)", UpdatePattern.Nothing),
	];
	private static readonly int[] Backgrounds = [0, 1_000, 5_000, 20_000, 50_000];

	private readonly MarketSheet[] _sheets;
	private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
	private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
	private readonly RollingStats _updates = new(capacity: 2_000);
	private readonly Button _start = new() { Content = "▶ Start" }, _pause = new() { Content = "❚❚ Pause" };
	private readonly StackPanel _sheetTabs = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(4, 2) };
	private readonly TextBlock _renderLabel = new(), _updateLabel = new(), _infoLabel = new() { HorizontalAlignment = HorizontalAlignment.Right };
	private bool _running, _everyFrame;
	private TickResult _lastTick;

	public TimedReoGridControl Grid { get; } = new();
	public MarketFeed Feed { get; }
	public MarketSheet Active { get; private set; }
	public IReadOnlyList<MarketSheet> Sheets => _sheets;
	public Workbook Workbook { get; }
	public double BuildMs { get; }
	public string LicenseState { get; }
	public RepaintCall Repaint { get; set; } = RepaintCall.InvalidateVisual;
	public RollingStats UpdateTimes => _updates;

	public MainWindow(Options options, string licenseState)
	{
		LicenseState = licenseState;
		Title = $"ReoGrid One — Live market grid on Avalonia ({options.Rows:N0} rows × {MarketSheet.ColumnCount} columns × 3 sheets)";
		Width = 1600;
		Height = 1000;
		WindowStartupLocation = WindowStartupLocation.CenterScreen;

		var sw = Stopwatch.StartNew();
		Workbook = new Workbook();
		_sheets = MarketSheet.CreateDemoSheets(Workbook, options.Rows);
		BuildMs = sw.Elapsed.TotalMilliseconds;
		Feed = new MarketFeed(_sheets) { BackgroundPerSheet = options.Background };
		Active = _sheets[0];

		Grid.LoadWorkbook(Workbook);
		Grid.SetWorksheet(Active.Sheet);

		_start.Click += (_, _) => SetRunning(true);
		_pause.Click += (_, _) => SetRunning(false);
		var flash = new CheckBox { Content = "Flash changes", IsChecked = Feed.Flash };
		flash.IsCheckedChanged += (_, _) => Feed.Flash = flash.IsChecked == true;

		var toolbar = new WrapPanel { Margin = new Thickness(6, 4) };
		foreach (var c in new Control[]
		{
			_start, _pause,
			Label("Tick"), Combo(Paces.Select(p => p.Label), 3, i => SetPace(Paces[i].Ms)),
			Label("Update"), Combo(Patterns.Select(p => p.Label), 0, i => Feed.Pattern = Patterns[i].Pattern),
			Label("Repaint with"), Combo(["InvalidateVisual()", "RefreshExternal()"], 0, i => Repaint = (RepaintCall)i),
			Label("Off-screen updates / sheet"), Combo(Backgrounds.Select(v => v.ToString("N0")), Array.IndexOf(Backgrounds, Feed.BackgroundPerSheet), i => Feed.BackgroundPerSheet = Backgrounds[i]),
			flash,
		})
		{
			if (c is not TextBlock) c.Margin = new Thickness(0, 0, 8, 0);
			c.VerticalAlignment = VerticalAlignment.Center;
			toolbar.Children.Add(c);
		}

		foreach (var s in _sheets)
		{
			var tab = new ToggleButton { Content = s.Title, Tag = s, Padding = new Thickness(14, 3) };
			tab.Click += (_, _) => SwitchTo((MarketSheet)tab.Tag!);
			_sheetTabs.Children.Add(tab);
		}

		var status = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Margin = new Thickness(8, 3) };
		_updateLabel.Margin = new Thickness(24, 0, 0, 0);
		Avalonia.Controls.Grid.SetColumn(_updateLabel, 1);
		Avalonia.Controls.Grid.SetColumn(_infoLabel, 2);
		status.Children.AddRange([_renderLabel, _updateLabel, _infoLabel]);
		foreach (var t in new[] { _renderLabel, _updateLabel, _infoLabel }) t.FontFamily = new FontFamily("Consolas, Menlo, monospace");

		var root = new DockPanel();
		DockPanel.SetDock(toolbar, Dock.Top);
		DockPanel.SetDock(status, Dock.Bottom);
		DockPanel.SetDock(_sheetTabs, Dock.Bottom);
		root.Children.AddRange([toolbar, status, _sheetTabs, Grid]);
		Content = root;

		_timer.Tick += (_, _) => TickOnce();
		_statusTimer.Tick += (_, _) => UpdateStatus();
		_statusTimer.Start();
		SyncSheetTabs();
		SetRunning(false);
		UpdateStatus();
	}

	public void SetRunning(bool running)
	{
		_running = running;
		_start.IsEnabled = !running;
		_pause.IsEnabled = running;
		_timer.IsEnabled = running && !_everyFrame;
		if (running && _everyFrame) RequestAnimationFrame(OnFrame);
	}

	/// <summary>0 = tick on every frame the display shows, as fast as frames come.</summary>
	public void SetPace(int ms)
	{
		_everyFrame = ms == 0;
		if (!_everyFrame) _timer.Interval = TimeSpan.FromMilliseconds(ms);
		SetRunning(_running);
	}

	private void OnFrame(TimeSpan _)
	{
		if (!_running || !_everyFrame) return;
		TickOnce();
		RequestAnimationFrame(OnFrame);
	}

	public void SwitchTo(MarketSheet sheet)
	{
		Active = sheet;
		Grid.SetWorksheet(sheet.Sheet);
		SyncSheetTabs();
	}

	/// <summary>One feed tick: write to the model, then ask for ONE repaint.</summary>
	public TickResult TickOnce()
	{
		var (first, last) = Grid.VisibleRows();
		_lastTick = Feed.Tick(Active, first, last);
		_updates.Add(_lastTick.UpdateMs);

		// The model has no "changed" event and the control does not watch it: after writing, ask for
		// a repaint yourself — once per batch, never per cell. InvalidateVisual() only marks the grid
		// dirty; Avalonia renders it once on the next frame however many times it was called.
		if (Repaint == RepaintCall.RefreshExternal) Grid.RefreshExternal();
		else Grid.InvalidateVisual();
		return _lastTick;
	}

	private void UpdateStatus()
	{
		var r = Grid.RenderTimes;
		_renderLabel.Text = $"Render {r.Last,5:F1} ms  avg {r.Average,4:F1} · p95 {r.Percentile(95),4:F1} · {Grid.FramesPerSecond,3} frames/s";
		_updateLabel.Text = $"Update {_lastTick.UpdateMs,5:F2} ms · {_lastTick.CellsWritten:N0} cells";
		var (first, last) = Grid.VisibleRows();
		_infoLabel.Text = $"rows {first:N0}–{last:N0} · build {BuildMs:N0} ms · {LicenseState}";
	}

	private void SyncSheetTabs()
	{
		foreach (var tab in _sheetTabs.Children.OfType<ToggleButton>()) tab.IsChecked = ReferenceEquals(tab.Tag, Active);
	}

	private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(8, 0, 4, 0) };

	private static ComboBox Combo(IEnumerable<string> items, int selected, Action<int> apply)
	{
		var combo = new ComboBox { ItemsSource = items.ToArray(), SelectedIndex = Math.Max(0, selected), MinWidth = 90 };
		combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) apply(combo.SelectedIndex); };
		return combo;
	}

	protected override void OnClosed(EventArgs e)
	{
		_timer.Stop();
		_statusTimer.Stop();
		base.OnClosed(e);
	}
}
