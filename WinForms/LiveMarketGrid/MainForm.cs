using System.Diagnostics;
using unvell.ReoGrid.Core;
using unvell.ReoGrid.WinForms;

namespace ReoGridSamples.LiveMarketGrid;

/// <summary>
/// A market-data monitor: three sheets of 100,000 instruments × 20 fields. The toolbar picks the
/// renderer (Direct2D or GDI+), how often the feed ticks and which cells a tick changes; the status
/// bar shows what each repaint costs (measured inside OnPaint), how many repaints reach the window
/// per second and what writing the tick into the model cost.
/// </summary>
internal sealed class MainForm : Form
{
	private static readonly (string Label, GridRenderer Renderer)[] Renderers = [("Direct2D", GridRenderer.Direct2D), ("GDI+", GridRenderer.Gdi)];
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
	private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
	private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 250 };
	private readonly FramePacer _pacer;
	private readonly PaintStats _updates = new(capacity: 240);
	private readonly ToolStripButton _start, _pause, _flash;
	private readonly ToolStripComboBox _renderer, _pace, _pattern, _background;
	private readonly ToolStrip _toolbar = new() { GripStyle = ToolStripGripStyle.Hidden };
	private readonly ToolStrip _sheetTabs = new() { Dock = DockStyle.Bottom, GripStyle = ToolStripGripStyle.Hidden };
	private readonly StatusStrip _status = new();
	private readonly ToolStripStatusLabel _paintLabel = new(), _updateLabel = new(), _infoLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleRight };
	private bool _running, _everyFrame;
	private TickResult _lastTick;

	public TimedReoGridControl Grid { get; } = new() { Dock = DockStyle.Fill };
	public MarketFeed Feed { get; }
	public MarketSheet Active { get; private set; }
	public IReadOnlyList<MarketSheet> Sheets => _sheets;
	public Workbook Workbook { get; }
	public double BuildMs { get; }
	public string LicenseState { get; }

	public MainForm(Options options, string licenseState)
	{
		LicenseState = licenseState;
		Text = $"ReoGrid One — Live market grid ({options.Rows:N0} rows × {MarketSheet.ColumnCount} columns × 3 sheets)";
		Size = new Size(1600, 1000);
		StartPosition = FormStartPosition.CenterScreen;

		var sw = Stopwatch.StartNew();
		Workbook = new Workbook();
		_sheets = MarketSheet.CreateDemoSheets(Workbook, options.Rows);
		BuildMs = sw.Elapsed.TotalMilliseconds;
		Feed = new MarketFeed(_sheets) { BackgroundPerSheet = options.Background };
		Active = _sheets[0];

		Grid.LoadWorkbook(Workbook);
		Grid.SetWorksheet(Active.Sheet);
		_pacer = new FramePacer(this, () => TickOnce());
		Grid.Painted += (_, _) => _pacer.FramePainted();

		_start = new ToolStripButton("▶ Start", null, (_, _) => SetRunning(true));
		_pause = new ToolStripButton("❚❚ Pause", null, (_, _) => SetRunning(false));
		_flash = new ToolStripButton("Flash changes") { CheckOnClick = true, Checked = Feed.Flash };
		_flash.CheckedChanged += (_, _) => Feed.Flash = _flash.Checked;
		_renderer = Combo(Renderers.Select(r => r.Label), Array.FindIndex(Renderers, r => r.Renderer == options.Renderer), 90,
			i => SetRenderer(Renderers[i].Renderer));
		_pace = Combo(Paces.Select(p => p.Label), 3, 100, i => SetPace(Paces[i].Ms));
		_pattern = Combo(Patterns.Select(p => p.Label), 0, 230, i => Feed.Pattern = Patterns[i].Pattern);
		_background = Combo(Backgrounds.Select(v => v.ToString("N0")), Array.IndexOf(Backgrounds, Feed.BackgroundPerSheet), 80,
			i => Feed.BackgroundPerSheet = Backgrounds[i]);

		_toolbar.Items.AddRange(new ToolStripItem[]
		{
			_start, _pause, new ToolStripSeparator(),
			new ToolStripLabel("Renderer"), _renderer, new ToolStripSeparator(),
			new ToolStripLabel("Tick"), _pace, new ToolStripSeparator(),
			new ToolStripLabel("Update"), _pattern, new ToolStripSeparator(),
			new ToolStripLabel("Off-screen updates / sheet"), _background, new ToolStripSeparator(),
			_flash,
		});

		foreach (var s in _sheets)
		{
			var tab = new ToolStripButton(s.Title) { Tag = s, CheckOnClick = false };
			tab.Click += (_, _) => SwitchTo((MarketSheet)tab.Tag!);
			_sheetTabs.Items.Add(tab);
		}

		_status.Items.AddRange(new ToolStripItem[] { _paintLabel, new ToolStripSeparator(), _updateLabel, _infoLabel });

		Controls.Add(Grid);
		Controls.Add(_sheetTabs);
		Controls.Add(_status);
		Controls.Add(_toolbar);

		SetRenderer(options.Renderer);
		_timer.Tick += (_, _) => TickOnce();
		_statusTimer.Tick += (_, _) => UpdateStatus();
		_statusTimer.Start();
		Shown += (_, _) => Grid.Focus();
		SyncSheetTabs();
		SetRunning(false);
		UpdateStatus();
	}

	/// <summary>
	/// Direct2D draws on the GPU and keeps each cell's laid-out text between frames, so a repaint
	/// costs little more than the cells that changed. Where it cannot run (no native DLL, an ARM64
	/// process, no Direct3D device) the control stays on GDI+ — <see cref="ReoGridControl.ActiveRenderer"/>
	/// says which one is drawing.
	/// </summary>
	public void SetRenderer(GridRenderer renderer)
	{
		Grid.Renderer = renderer;
		Grid.PaintTimes.Clear();
		Grid.Invalidate();
	}

	public void SetRunning(bool running)
	{
		_running = running;
		_start.Enabled = !running;
		_pause.Enabled = running;
		_timer.Enabled = running && !_everyFrame;
		if (running && _everyFrame) _pacer.Start(); else _pacer.Stop();
	}

	/// <summary>0 = tick on every frame the display shows.</summary>
	public void SetPace(int ms)
	{
		_everyFrame = ms == 0;
		if (!_everyFrame) _timer.Interval = ms;
		SetRunning(_running);
	}

	public void SwitchTo(MarketSheet sheet)
	{
		Active = sheet;
		Grid.SetWorksheet(sheet.Sheet);
		SyncSheetTabs();
	}

	/// <summary>The toolbar, sheet tabs and status bar; <c>--demo</c> hides them.</summary>
	public void ShowChrome(bool visible)
	{
		_toolbar.Visible = _sheetTabs.Visible = _status.Visible = visible;
	}

	/// <summary>Raised at the start of every tick, before the feed reads the visible rows.</summary>
	public event Action? Ticking;

	/// <summary>One feed tick: write to the model, then ask for ONE repaint.</summary>
	public TickResult TickOnce()
	{
		Ticking?.Invoke();
		var (first, last) = Grid.VisibleRows();
		_lastTick = Feed.Tick(Active, first, last);
		_updates.Add(_lastTick.UpdateMs);

		// The model has no "changed" event and the control does not watch it: after writing,
		// request a repaint yourself — once per batch, never per cell. Invalidate() only marks
		// the window dirty; Windows folds every request into one WM_PAINT.
		// Refresh() or Update() per cell would paint the whole view once per cell instead.
		Grid.Invalidate();
		return _lastTick;
	}

	public PaintStats UpdateTimes => _updates;

	private void UpdateStatus()
	{
		var p = Grid.PaintTimes;
		string renderer = Grid.ActiveRenderer == GridRenderer.Direct2D ? "Direct2D"
			: Grid.Renderer == GridRenderer.Direct2D ? "GDI+ (Direct2D unavailable)" : "GDI+";
		_paintLabel.Text = $"{renderer} · Paint {p.Last,5:F1} ms   avg {p.Average:F1} · p95 {p.Percentile(95):F1} · {Grid.FramesPerSecond,3} frames/s";
		_updateLabel.Text = $"Update {_lastTick.UpdateMs:F2} ms · {_lastTick.CellsWritten:N0} cells";
		var (first, last) = Grid.VisibleRows();
		_infoLabel.Text = $"Visible rows {first:N0}–{last:N0} · build {BuildMs:N0} ms · {LicenseState}";
	}

	private void SyncSheetTabs()
	{
		foreach (ToolStripButton tab in _sheetTabs.Items) tab.Checked = ReferenceEquals(tab.Tag, Active);
	}

	private static ToolStripComboBox Combo(IEnumerable<string> items, int selected, int width, Action<int> apply)
	{
		var combo = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = width };
		foreach (string item in items) combo.Items.Add(item);
		combo.SelectedIndex = Math.Max(0, selected);
		combo.SelectedIndexChanged += (_, _) => apply(combo.SelectedIndex);
		return combo;
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		_pacer.Dispose();
		_timer.Dispose();
		_statusTimer.Dispose();
		base.OnFormClosed(e);
	}
}
