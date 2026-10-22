using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using unvell.ReoGrid.Avalonia;

namespace ReoGridSamples.LiveMarketGrid;

/// <summary>
/// <c>--bench</c>: opens the demo window maximized and, phase by phase, ticks the feed on every frame
/// the display shows — the fastest an application can update — recording the model update, the
/// grid's <c>Render</c> on the UI thread and the interval between frames. Then it paints exactly
/// 100 rows × 20 columns off screen, layout and rasterization together on one thread, so that
/// result does not depend on how many rows fit on the monitor running it.
/// </summary>
internal static class Benchmark
{
	private const int OffscreenRows = 100;

	private sealed record OnScreenPhase(string Name, RepaintCall Call, string Window, double[] Update, double[] Render, double[] Frame);

	private sealed record OffscreenPhase(string Name, double Scale, double[] Update, double[] Frame);

	public static async Task<int> RunAsync(MainWindow w, Options o)
	{
		if (OperatingSystem.IsWindows() && !Console.IsOutputRedirected) AttachConsole(-1);

		w.WindowState = WindowState.Maximized;
		for (int i = 0; i < 30; i++) await NextFrame(w);   // let the window settle at its final size

		// Warm up every pattern once, so the first measured phase does not also pay for JIT
		// compilation and the first layouts of every text on screen.
		foreach (var pattern in Enum.GetValues<UpdatePattern>())
		{
			w.Feed.Pattern = pattern;
			for (int i = 0; i < 60; i++) { w.TickOnce(); await NextFrame(w); }
		}

		var grid = w.Grid;
		var onScreen = new List<OnScreenPhase>
		{
			await OnScreen(w, "Clock column + 8 prices", UpdatePattern.ClockAndFew, RepaintCall.RefreshExternal, o.Frames),
			await OnScreen(w, "Clock column + 8 prices", UpdatePattern.ClockAndFew, RepaintCall.InvalidateVisual, o.Frames),
			await OnScreen(w, "Market feed: ~85% of visible instruments", UpdatePattern.Market, RepaintCall.InvalidateVisual, o.Frames),
			await OnScreen(w, "Every visible instrument", UpdatePattern.EveryVisible, RepaintCall.InvalidateVisual, o.Frames),
			await OnScreen(w, "Market feed while scrolling 3 rows per frame", UpdatePattern.Market, RepaintCall.InvalidateVisual, o.Frames, _ =>
			{
				double rowH = w.Active.Sheet.Rows.DefaultSize;
				double max = w.Active.Sheet.RowCount * rowH - 2_000;
				grid.Viewport.ScrollY = (grid.Viewport.ScrollY + 3 * rowH) % max;
			}),
			await OnScreen(w, "Nothing changes (repaint only)", UpdatePattern.Nothing, RepaintCall.InvalidateVisual, o.Frames),
		};

		double scale = w.RenderScaling;
		var offscreen = new List<OffscreenPhase>
		{
			Offscreen(w, "Clock column + 8 prices", UpdatePattern.ClockAndFew, o.Frames, 1),
			Offscreen(w, "Market feed", UpdatePattern.Market, o.Frames, 1),
			Offscreen(w, "Every visible instrument", UpdatePattern.EveryVisible, o.Frames, 1),
		};
		if (scale != 1) offscreen.Add(Offscreen(w, "Market feed", UpdatePattern.Market, o.Frames, scale));

		string report = Report(w, o, onScreen, offscreen);
		string path = o.Out ?? Path.Combine(AppContext.BaseDirectory, "bench-results.md");
		File.WriteAllText(path, report, Encoding.UTF8);
		Console.WriteLine(report);
		Console.WriteLine($"Report written to {path}");
		return 0;
	}

	private static async Task<OnScreenPhase> OnScreen(MainWindow w, string name, UpdatePattern pattern, RepaintCall call,
		int frames, Action<int>? before = null)
	{
		var grid = w.Grid;
		w.Feed.Pattern = pattern;
		w.Repaint = call;
		for (int i = 0; i < 30; i++) { before?.Invoke(i); w.TickOnce(); await NextFrame(w); }   // warm-up

		grid.RenderTimes.Clear();
		w.UpdateTimes.Clear();
		var frame = new List<double>(frames);
		long last = Stopwatch.GetTimestamp();
		for (int i = 0; i < frames; i++)
		{
			before?.Invoke(i);
			w.TickOnce();
			await NextFrame(w);          // the tick above is rendered in this frame
			long now = Stopwatch.GetTimestamp();
			frame.Add(Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
			last = now;
		}
		var (first, lastRow) = grid.VisibleRows();
		string window = $"{lastRow - first + 1 + MarketSheet.HeaderRows} × {MarketSheet.ColumnCount}";
		return new OnScreenPhase(name, call, window, w.UpdateTimes.Snapshot(), grid.RenderTimes.Snapshot(), frame.ToArray());
	}

	// Ticks inside the frame callback, as an application updating "every frame" would: the grid
	// renders that tick later in the same frame.
	private static Task NextFrame(TopLevel top)
	{
		var done = new TaskCompletionSource();
		top.RequestAnimationFrame(_ => done.SetResult());
		return done.Task;
	}

	// Exactly 100 data rows × 20 columns, drawn by the control into a bitmap: Render's layout and the
	// Skia rasterization both land in the measured time.
	private static OffscreenPhase Offscreen(MainWindow w, string name, UpdatePattern pattern, int ticks, double scale)
	{
		var grid = w.Grid;
		w.Feed.Pattern = pattern;
		w.SwitchTo(w.Sheets[0]);
		var vp = grid.Viewport;
		var ws = w.Active.Sheet;
		vp.ScrollY = 10_000 * ws.Rows.DefaultSize;

		double width = vp.RowHeaderWidth, height = vp.ColumnHeaderHeight + (OffscreenRows + MarketSheet.HeaderRows) * ws.Rows.DefaultSize;
		for (int c = 0; c < MarketSheet.ColumnCount; c++) width += ws.Columns.GetSize(c);
		using var target = new RenderTargetBitmap(
			new PixelSize((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale)), new Vector(96 * scale, 96 * scale));
		void Paint()
		{
			using var ctx = target.CreateDrawingContext();
			grid.RenderTo(ctx, width, height);
		}

		var (first, _) = vp.VisibleRows(height);
		int last = first + OffscreenRows - 1;
		for (int i = 0; i < 10; i++) { w.Feed.Tick(w.Active, first, last); Paint(); }

		var update = new double[ticks];
		var frame = new double[ticks];
		for (int i = 0; i < ticks; i++)
		{
			update[i] = w.Feed.Tick(w.Active, first, last).UpdateMs;
			long t0 = Stopwatch.GetTimestamp();
			Paint();
			frame[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
		}
		target.Save(Path.Combine(AppContext.BaseDirectory, $"bench-frame-{pattern}-{scale * 100:F0}.png"));
		return new OffscreenPhase(name, scale, update, frame);
	}

	// ---- report -------------------------------------------------------------------------

	private static string Report(MainWindow w, Options o, List<OnScreenPhase> onScreen, List<OffscreenPhase> offscreen)
	{
		var sb = new StringBuilder();
		var ws = w.Sheets[0].Sheet;

		sb.AppendLine("# LiveMarketGrid (Avalonia) benchmark");
		sb.AppendLine();
		sb.AppendLine($"- Date: {DateTime.Now:yyyy-MM-dd HH:mm}");
		sb.AppendLine($"- Machine: {Cpu()} ({Environment.ProcessorCount} logical), {RuntimeInformation.OSDescription}");
		sb.AppendLine($"- Runtime: .NET {Environment.Version}, {(System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation")} GC, {(Debugger.IsAttached ? "debugger attached" : "no debugger")}, {Configuration()}");
		sb.AppendLine($"- ReoGrid: {Version(typeof(ReoGridControl).Assembly)} — {w.LicenseState}; Avalonia {Version(typeof(AvaloniaObject).Assembly)}");
		sb.AppendLine($"- Window: grid {w.Grid.Bounds.Width:F0}×{w.Grid.Bounds.Height:F0} DIP at {w.RenderScaling * 100:F0}% scale");
		sb.AppendLine($"- Model: {w.Sheets.Count} sheets × {o.Rows:N0} rows × {MarketSheet.ColumnCount} columns, built in {w.BuildMs:N0} ms");
		sb.AppendLine($"- Per sheet: {ws.Merges.Count:N0} merged ranges, {ws.CellTypes.Count:N0} cell-type ranges, {ws.DistinctStyleCount:N0} distinct styles");
		sb.AppendLine($"- Market feed per tick: ~85% of the visible instruments + {o.Background:N0} random instruments on each of the {w.Sheets.Count} sheets, 15 fields each; moved prices flash");
		sb.AppendLine();
		sb.AppendLine("## On screen, one tick per frame");
		sb.AppendLine();
		sb.AppendLine("The feed ticks on every frame the display shows — the fastest an application can update. Times in ms,");
		sb.AppendLine("median / p95. *Update* is the application writing the tick into the model; *Render* is the grid's");
		sb.AppendLine("`Render` on the UI thread (layout and recording; Avalonia rasterizes on its render thread); *Frame* is the");
		sb.AppendLine("interval between frames, which includes everything and is capped by the display's refresh rate.");
		sb.AppendLine();
		sb.AppendLine("| Update pattern | Repaint call | Visible | Update | Render | Frame | Frames/s |");
		sb.AppendLine("|---|---|---|---:|---:|---:|---:|");
		foreach (var p in onScreen)
			sb.AppendLine($"| {p.Name} | `{p.Call}()` | {p.Window} | {Stat(p.Update)} | {Stat(p.Render)} | {Stat(p.Frame)} | {1000 / Median(p.Frame):F0} |");
		sb.AppendLine();
		sb.AppendLine($"## Off screen, exactly {OffscreenRows} rows × {MarketSheet.ColumnCount} columns");
		sb.AppendLine();
		sb.AppendLine("The control draws into a bitmap: layout and Skia rasterization on one thread, so *Frame* is the whole");
		sb.AppendLine("cost of one repaint of a 100 × 20 window, whatever the monitor.");
		sb.AppendLine();
		sb.AppendLine("| Update pattern | Scale | Update | Frame |");
		sb.AppendLine("|---|---:|---:|---:|");
		foreach (var p in offscreen)
			sb.AppendLine($"| {p.Name} | {p.Scale * 100:F0}% | {Stat(p.Update)} | {Stat(p.Frame)} |");
		sb.AppendLine();
		return sb.ToString();
	}

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

	private static string Version(Assembly asm) =>
		asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
			?? asm.GetName().Version?.ToString() ?? "?";

	private static string Cpu()
	{
		try
		{
			if (OperatingSystem.IsWindows()
				&& Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) is string s)
				return s.Trim();
			if (OperatingSystem.IsLinux())
				foreach (string line in File.ReadLines("/proc/cpuinfo"))
					if (line.StartsWith("model name")) return line[(line.IndexOf(':') + 1)..].Trim();
		}
		catch (Exception) { }
		return RuntimeInformation.ProcessArchitecture.ToString();
	}

	private static string Configuration() =>
		typeof(Benchmark).Assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled == true ? "Debug build" : "Release build";

	[DllImport("kernel32.dll")]
	private static extern bool AttachConsole(int processId);
}
