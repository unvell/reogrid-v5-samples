using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using unvell.ReoGrid.WinForms;

namespace ReoGridSamples.LiveMarketGrid;

/// <summary>
/// <c>--bench</c>: opens the demo window maximized and, for each renderer (GDI+, then Direct2D) and
/// each update pattern, drives the feed back to back — tick, repaint, next tick — recording the
/// application-side update time, the repaint time measured inside OnPaint and the whole tick.
/// Then it ticks once per frame the display shows, as an application updating "every frame" would,
/// and counts the repaints that reach the window per second.
/// </summary>
internal static class Benchmark
{
	private const double PacedSeconds = 3;

	private sealed record Phase(GridRenderer Renderer, string Name, string Window, double[] Update, double[] Paint, double[] Tick);

	private sealed record PacedPhase(GridRenderer Renderer, string Name, double[] Paint, double FramesPerSecond);

	public static int Run(Options o, string license)
	{
		if (!Console.IsOutputRedirected) AttachConsole(-1);

		using var form = new MainForm(o, license);
		form.WindowState = FormWindowState.Maximized;
		form.Show();
		Application.DoEvents();
		var grid = form.Grid;

		var phases = new List<Phase>();
		var paced = new List<PacedPhase>();
		var skipped = new List<string>();
		foreach (var renderer in new[] { GridRenderer.Gdi, GridRenderer.Direct2D })
		{
			form.SetRenderer(renderer);
			form.SwitchTo(form.Sheets[0]);
			grid.Viewport.ScrollY = 0;
			grid.Refresh();
			if (grid.ActiveRenderer != renderer)
			{
				skipped.Add($"{renderer} is not available on this machine, so it was not measured.");
				continue;
			}

			// Warm up every pattern once, so the first measured phase does not also pay for JIT
			// compilation and the first layout of every text on screen.
			foreach (var pattern in Enum.GetValues<UpdatePattern>())
			{
				form.Feed.Pattern = pattern;
				for (int i = 0; i < 30; i++) { form.TickOnce(); grid.Update(); Application.DoEvents(); }
			}

			phases.Add(OnScreen(form, "Clock column + 8 prices", UpdatePattern.ClockAndFew, o.Ticks));
			phases.Add(OnScreen(form, "Market feed: ~85% of visible instruments", UpdatePattern.Market, o.Ticks));
			phases.Add(OnScreen(form, "Every visible instrument", UpdatePattern.EveryVisible, o.Ticks));
			phases.Add(OnScreen(form, "Market feed while scrolling 3 rows per tick", UpdatePattern.Market, o.Ticks, _ =>
			{
				double rowH = form.Active.Sheet.Rows.DefaultSize;
				double max = form.Active.Sheet.RowCount * rowH - 2_000;
				grid.Viewport.ScrollY = (grid.Viewport.ScrollY + 3 * rowH) % max;
			}));
			phases.Add(OnScreen(form, "Nothing changes (repaint only)", UpdatePattern.Nothing, o.Ticks));

			grid.Viewport.ScrollY = 0;
			paced.Add(Paced(form, "Clock column + 8 prices", UpdatePattern.ClockAndFew));
			paced.Add(Paced(form, "Market feed: ~85% of visible instruments", UpdatePattern.Market));
			paced.Add(Paced(form, "Every visible instrument", UpdatePattern.EveryVisible));
			paced.Add(Paced(form, "Nothing changes (repaint only)", UpdatePattern.Nothing));
		}

		string report = Report(form, o, phases, paced, skipped);
		string path = o.Out ?? Path.Combine(AppContext.BaseDirectory, "bench-results.md");
		File.WriteAllText(path, report, Encoding.UTF8);
		Console.WriteLine(report);
		Console.WriteLine($"Report written to {path}");
		form.Close();
		return 0;
	}

	private static Phase OnScreen(MainForm form, string name, UpdatePattern pattern, int ticks, Action<int>? before = null)
	{
		var grid = form.Grid;
		form.Feed.Pattern = pattern;
		for (int i = 0; i < 10; i++) { before?.Invoke(i); form.TickOnce(); grid.Update(); Application.DoEvents(); }   // warm-up

		var update = new List<double>(ticks);
		var paint = new List<double>(ticks);
		var total = new List<double>(ticks);
		for (int i = 0; i < ticks; i++)
		{
			before?.Invoke(i);
			long paints = grid.PaintTimes.Total;
			long t0 = Stopwatch.GetTimestamp();
			var r = form.TickOnce();
			grid.Update();      // bench only: run the pending WM_PAINT now so it lands in this tick
			total.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
			update.Add(r.UpdateMs);
			if (grid.PaintTimes.Total > paints) paint.Add(grid.PaintTimes.Last);
			Application.DoEvents();
		}
		var (first, last) = grid.VisibleRows();
		string window = $"{last - first + 1 + MarketSheet.HeaderRows} × {MarketSheet.ColumnCount}";
		return new Phase(grid.ActiveRenderer, name, window, update.ToArray(), paint.ToArray(), total.ToArray());
	}

	// One tick per frame the display shows (FramePacer), for a few seconds: how many repaints reach
	// the window per second, and what each costs.
	private static PacedPhase Paced(MainForm form, string name, UpdatePattern pattern)
	{
		var grid = form.Grid;
		form.Feed.Pattern = pattern;
		form.SetPace(0);
		form.SetRunning(true);
		Pump(1);
		grid.PaintTimes.Clear();
		var sw = Stopwatch.StartNew();
		Pump(PacedSeconds);
		double fps = grid.PaintTimes.Total / sw.Elapsed.TotalSeconds;
		form.SetRunning(false);
		form.SetPace(250);
		Pump(0.2);
		return new PacedPhase(grid.ActiveRenderer, name, grid.PaintTimes.Snapshot(), fps);
	}

	private static void Pump(double seconds)
	{
		var sw = Stopwatch.StartNew();
		while (sw.Elapsed.TotalSeconds < seconds)
		{
			Application.DoEvents();
			WaitMessage();
		}
	}

	// ---- report -------------------------------------------------------------------------

	private static string Report(MainForm form, Options o, List<Phase> phases, List<PacedPhase> paced, List<string> skipped)
	{
		var sb = new StringBuilder();
		var ws = form.Sheets[0].Sheet;
		var asm = typeof(ReoGridControl).Assembly;
		string version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
			?? asm.GetName().Version?.ToString() ?? "?";
		var screen = Screen.FromControl(form);

		sb.AppendLine("# LiveMarketGrid (WinForms) benchmark");
		sb.AppendLine();
		sb.AppendLine($"- Date: {DateTime.Now:yyyy-MM-dd HH:mm}");
		sb.AppendLine($"- Machine: {Cpu()} ({Environment.ProcessorCount} logical), {RuntimeInformation.OSDescription}");
		sb.AppendLine($"- Runtime: .NET {Environment.Version}, {(System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation")} GC, {(Debugger.IsAttached ? "debugger attached" : "no debugger")}, {Configuration()}");
		sb.AppendLine($"- ReoGrid: {asm.GetName().Name} {version} — {form.LicenseState}");
		sb.AppendLine($"- Display: {screen.Bounds.Width}×{screen.Bounds.Height} px at {form.Grid.DpiScale * 100:F0}% scale; grid {form.Grid.ClientSize.Width}×{form.Grid.ClientSize.Height} px");
		sb.AppendLine($"- Model: {form.Sheets.Count} sheets × {o.Rows:N0} rows × {MarketSheet.ColumnCount} columns, built in {form.BuildMs:N0} ms");
		sb.AppendLine($"- Per sheet: {ws.Merges.Count:N0} merged ranges, {ws.CellTypes.Count:N0} cell-type ranges, {ws.DistinctStyleCount:N0} distinct styles");
		sb.AppendLine($"- Market feed per tick: ~85% of the visible instruments + {o.Background:N0} random instruments on each of the {form.Sheets.Count} sheets, 15 fields each; moved prices flash");
		foreach (string s in skipped) sb.AppendLine($"- {s}");
		sb.AppendLine();
		sb.AppendLine("Ticks run back to back: write the tick into the model, repaint, next tick. Times in ms, median / p95.");
		sb.AppendLine("*Update* is the application writing the tick into the model. *Paint* is the repaint timed inside");
		sb.AppendLine("`OnPaint` — with Direct2D it includes presenting the frame, with GDI+ it does not include copying the");
		sb.AppendLine("back buffer to the window. *Tick* is update, repaint and presentation together; *Ticks/s* is the most");
		sb.AppendLine("the UI thread could do, of which the display shows at most its refresh rate.");
		sb.AppendLine();
		sb.AppendLine("## Back to back");
		sb.AppendLine();
		sb.AppendLine("| Renderer | Update pattern | Visible | Update | Paint | Tick | Ticks/s |");
		sb.AppendLine("|---|---|---|---:|---:|---:|---:|");
		foreach (var p in phases)
			sb.AppendLine($"| {Name(p.Renderer)} | {p.Name} | {p.Window} | {Stat(p.Update)} | {Stat(p.Paint)} | {Stat(p.Tick)} | {1000 / Median(p.Tick):F0} |");
		sb.AppendLine();
		sb.AppendLine("## One tick per display frame");
		sb.AppendLine();
		sb.AppendLine($"The feed ticks once for every frame the display shows, for {PacedSeconds:F0} seconds. *Frames/s* counts the");
		sb.AppendLine("repaints that reached the window; the display's refresh rate is the most it can be.");
		sb.AppendLine();
		sb.AppendLine("| Renderer | Update pattern | Paint | Frames/s |");
		sb.AppendLine("|---|---|---:|---:|");
		foreach (var p in paced)
			sb.AppendLine($"| {Name(p.Renderer)} | {p.Name} | {Stat(p.Paint)} | {p.FramesPerSecond:F0} |");
		sb.AppendLine();
		return sb.ToString();
	}

	private static string Name(GridRenderer r) => r == GridRenderer.Gdi ? "GDI+" : "Direct2D";

	private static string Stat(double[] v)
	{
		if (v.Length == 0) return "–";
		var s = (double[])v.Clone();
		Array.Sort(s);
		return $"{s[s.Length / 2]:F2} / {s[Math.Min(s.Length - 1, (int)Math.Ceiling(s.Length * 0.95) - 1)]:F2}";
	}

	private static double Median(double[] v)
	{
		var s = (double[])v.Clone();
		Array.Sort(s);
		return s.Length == 0 ? double.NaN : s[s.Length / 2];
	}

	private static string Cpu() =>
		Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) is string s
			? s.Trim() : "unknown CPU";

	private static string Configuration() =>
		typeof(Benchmark).Assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled == true ? "Debug build" : "Release build";

	[DllImport("kernel32.dll")]
	private static extern bool AttachConsole(int processId);

	[DllImport("user32.dll")]
	private static extern bool WaitMessage();
}
