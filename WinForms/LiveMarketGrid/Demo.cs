using System.Diagnostics;
using unvell.ReoGrid.WinForms;

namespace ReoGridSamples.LiveMarketGrid;

/// <summary>
/// <c>--demo</c>: a scripted, full-screen run made to be recorded as a video. Three rounds — every
/// visible cell changing on every frame, scrolling while they change, a clock column with a few
/// prices — each played on GDI+ and then on Direct2D, ticking once for every frame the display
/// shows. The band above the grid (<see cref="DemoHud"/>) shows the renderer, the paint time and
/// the frame rate; the run ends with the numbers it measured. Esc ends it early.
/// </summary>
/// <remarks>
/// <c>--sync</c> is for a recording script: the demo prints <c>ready</c> when the title card is on
/// screen and waits for a line on standard input before it goes on; at the end it prints
/// <c>done</c> and waits for another line before it closes the window, so the recorder can stop
/// while the window is still there.
/// </remarks>
internal sealed class Demo
{
	private const double ScrollRowsPerSecond = 30;
	private const double SettleSeconds = 1;   // after a switch, before measuring
	private const double TitleSeconds = 5;

	private sealed record Scene(GridRenderer Renderer, double Seconds, string Caption);

	private sealed record Round(string Title, string Label, UpdatePattern Pattern, bool Scroll, Scene Gdi, Scene Direct2D);

	private sealed record Measured(GridRenderer Renderer, double Paint, double FramesPerSecond);

	private static readonly Round[] Rounds =
	[
		new("Every visible cell, every frame", "Every visible cell changes on every frame", UpdatePattern.EveryVisible, false,
			new(GridRenderer.Gdi, 11, "GDI+ draws on the CPU, and lays out the text of every visible cell on every repaint."),
			new(GridRenderer.Direct2D, 11, "Direct2D draws on the GPU. Same grid, same updates.")),
		new("Scrolling while it updates", "Scrolling while every visible cell changes", UpdatePattern.EveryVisible, true,
			new(GridRenderer.Gdi, 8, $"GDI+: scrolling {ScrollRowsPerSecond:F0} rows a second, every visible cell still changing."),
			new(GridRenderer.Direct2D, 8, "Direct2D: the same scroll, the same updates.")),
		new("A clock column and 8 prices", "Clock column + 8 prices change", UpdatePattern.ClockAndFew, false,
			new(GridRenderer.Gdi, 9, "Only the clock column and 8 prices change, but GDI+ still lays out every visible cell."),
			new(GridRenderer.Direct2D, 9, "Direct2D keeps each cell's laid-out text between frames: only the changed cells are laid out again.")),
	];

	private readonly MainForm _form;
	private readonly Options _options;
	private readonly Screen _screen;
	private readonly DemoHud _hud;
	private readonly CancellationTokenSource _closing = new();
	private DemoCard? _card;
	private bool _scrolling;
	private long _scrollStart;

	public static int Run(Options options, string license)
	{
		using var form = new MainForm(options, license);
		_ = new Demo(form, options);
		Application.Run(form);
		return 0;
	}

	private Demo(MainForm form, Options options)
	{
		_form = form;
		_options = options;
		_screen = Screen.FromPoint(Cursor.Position);

		form.FormBorderStyle = FormBorderStyle.None;
		form.StartPosition = FormStartPosition.Manual;
		form.Bounds = _screen.Bounds;
		form.ShowChrome(false);
		form.Feed.Flash = false;   // a flash on every frame would strobe
		form.KeyPreview = true;
		form.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) form.Close(); };
		form.FormClosing += (_, _) => _closing.Cancel();
		form.Ticking += ScrollOnTick;

		_hud = new DemoHud(form.Grid, SystemInfo.RefreshRate(_screen));
		form.Controls.Add(_hud);
		form.Shown += async (_, _) => await PlayAsync();
	}

	private async Task PlayAsync()
	{
		var token = _closing.Token;
		try
		{
			ZoomToWidth();
			_hud.SetInfo($"ReoGrid One {SystemInfo.ReoGridVersion()} · WinForms · {_form.Sheets.Count} sheets × {_options.Rows:N0} instruments"
				+ $" × {MarketSheet.ColumnCount} fields · {_screen.Bounds.Width} × {_screen.Bounds.Height}, zoom {_form.Grid.Zoom:P0}");
			_form.SetPace(0);
			_form.SetRunning(true);

			// Run every round once on both renderers first, so the recording does not show the JIT.
			_hud.SetCaption("Warming up", "");
			foreach (var renderer in new[] { GridRenderer.Direct2D, GridRenderer.Gdi })
				foreach (var round in Rounds)
				{
					Prepare(round, renderer);
					await Wait(0.5, token);
				}
			_form.SetRunning(false);
			_scrolling = false;
			_form.Grid.Viewport.ScrollY = 0;

			var title = ShowCard(TitleCard(), TitleSeconds);
			if (_options.Sync)
			{
				Console.WriteLine("ready");
				await Task.Run(Console.ReadLine, token);
			}
			title.Start();
			await Wait(TitleSeconds, token);

			ShowGrid();
			_hud.Reset();
			_form.SetRunning(true);
			var results = new List<(Round Round, Measured Gdi, Measured Direct2D)>();
			foreach (var round in Rounds)
			{
				var gdi = await Play(round, round.Gdi, token);
				var d2d = await Play(round, round.Direct2D, token);
				results.Add((round, gdi, d2d));
			}
			_form.SetRunning(false);
			_scrolling = false;

			await Card(CodeCard(), 7, token);
			await Card(ResultsCard(results), 10, token);
			if (_options.Sync)
			{
				Console.WriteLine("done");
				await Task.Run(Console.ReadLine, token);
			}
			_form.Close();
		}
		catch (OperationCanceledException) { }
	}

	private void Prepare(Round round, GridRenderer renderer)
	{
		_form.SetRenderer(renderer);
		_form.Feed.Pattern = round.Pattern;
		_form.Grid.Viewport.ScrollY = 0;
		_scrolling = round.Scroll;
		_scrollStart = Stopwatch.GetTimestamp();
	}

	/// <summary>Plays one scene; measures it after the first second, once the switch has settled.</summary>
	private async Task<Measured> Play(Round round, Scene scene, CancellationToken token)
	{
		Prepare(round, scene.Renderer);
		_hud.SetCaption(round.Title, scene.Caption);
		await Wait(SettleSeconds, token);

		var grid = _form.Grid;
		var paints = new List<double>();
		EventHandler record = (_, _) => paints.Add(grid.PaintTimes.Last);
		grid.Painted += record;
		long t0 = Stopwatch.GetTimestamp();
		try { await Wait(scene.Seconds - SettleSeconds, token); }
		finally { grid.Painted -= record; }

		double seconds = Stopwatch.GetElapsedTime(t0).TotalSeconds;
		paints.Sort();
		return new Measured(grid.ActiveRenderer, paints.Count == 0 ? double.NaN : paints[paints.Count / 2], paints.Count / seconds);
	}

	// Scrolls by time, not by tick: the same speed whatever the frame rate, so a slower renderer
	// shows as bigger jumps between frames.
	private void ScrollOnTick()
	{
		if (!_scrolling) return;
		double rowHeight = _form.Active.Sheet.Rows.DefaultSize;
		_form.Grid.Viewport.ScrollY = Stopwatch.GetElapsedTime(_scrollStart).TotalSeconds * ScrollRowsPerSecond * rowHeight;
	}

	// Zoom so that the columns fill the screen's width: no empty space beside the sheet, and the
	// cells read better in a video.
	private void ZoomToWidth()
	{
		var grid = _form.Grid;
		double available = (grid.ClientSize.Width - SystemInformation.GetVerticalScrollBarWidthForDpi(grid.DeviceDpi)) / grid.DpiScale;
		grid.Zoom = Math.Floor(available / (grid.Viewport.RowHeaderWidth + grid.Viewport.ContentWidth) * 100) / 100;
	}

	private static Task Wait(double seconds, CancellationToken token) => Task.Delay(TimeSpan.FromSeconds(seconds), token);

	private async Task Card(DemoCardContent content, double seconds, CancellationToken token)
	{
		ShowCard(content, seconds).Start();
		await Wait(seconds, token);
	}

	private DemoCard ShowCard(DemoCardContent content, double seconds)
	{
		_card?.Dispose();
		_card = new DemoCard(content, seconds);
		_form.Grid.Visible = _hud.Visible = false;
		_form.Controls.Add(_card);
		return _card;
	}

	private void ShowGrid()
	{
		_card?.Dispose();
		_card = null;
		_hud.Visible = _form.Grid.Visible = true;
		_form.Grid.Focus();
	}

	// ---- cards --------------------------------------------------------------------------

	private int RefreshRate => SystemInfo.RefreshRate(_screen);

	private DemoCardContent TitleCard() => new()
	{
		Kicker = $"ReoGrid One {SystemInfo.ReoGridVersion()}",
		Title = "Direct2D: GPU rendering for WinForms",
		Lines =
		[
			$"A live market-data grid on a {_screen.Bounds.Width} × {_screen.Bounds.Height} display: {_form.Sheets.Count} sheets × {_options.Rows:N0} instruments × {MarketSheet.ColumnCount} fields,"
				+ $" updated once for every frame the display shows{(RefreshRate > 0 ? $" ({RefreshRate} Hz)" : "")}.",
			"First on GDI+, the WinForms default, then on Direct2D: the same grid, the same updates.",
		],
	};

	private static DemoCardContent CodeCard() => new()
	{
		Kicker = "How to switch",
		Title = "One property",
		Code = ["grid.Renderer = GridRenderer.Direct2D;   // the default is GridRenderer.Gdi"],
		Lines =
		[
			"The native Direct2D DLLs ship in the unvell.ReoGrid.One package. There is nothing else to install.",
			"Where Direct2D cannot run (an ARM64 process, no Direct3D device), the control stays on GDI+, and ActiveRenderer says so.",
		],
	};

	private DemoCardContent ResultsCard(List<(Round Round, Measured Gdi, Measured Direct2D)> results)
	{
		static string Cell(Measured m) => double.IsNaN(m.Paint) ? "–" : $"{m.Paint:F1} ms · {m.FramesPerSecond:F0} fps";

		var table = new List<string[]> { new[] { "", "GDI+", "Direct2D", "Direct2D paints" } };
		foreach (var (round, gdi, d2d) in results)
			table.Add([round.Label, Cell(gdi), Cell(d2d),
				d2d.Renderer == GridRenderer.Direct2D ? $"{gdi.Paint / d2d.Paint:F1}× faster" : "Direct2D unavailable"]);

		return new DemoCardContent
		{
			Kicker = "Measured in this run",
			Title = "GDI+ and Direct2D, the same grid",
			Table = [.. table],
			TableColors = [DemoHud.Soft, DemoHud.GdiColor, DemoHud.Direct2DColor, Color.White],
			Footer =
			[
				"Median paint time per frame, timed inside OnPaint (with Direct2D it includes presenting the frame). fps counts the repaints that reached the window.",
				$"{SystemInfo.Cpu()} · {SystemInfo.Gpu(_screen)} · {_screen.Bounds.Width} × {_screen.Bounds.Height} at {_form.Grid.DpiScale * 100:F0}%"
					+ $"{(RefreshRate > 0 ? $", {RefreshRate} Hz" : "")}, zoom {_form.Grid.Zoom:P0} · {SystemInfo.Windows()} · .NET {Environment.Version} · ReoGrid One {SystemInfo.ReoGridVersion()}",
				"Sample: github.com/unvell/reogrid-v5-samples (WinForms/LiveMarketGrid, --demo)",
			],
		};
	}
}
