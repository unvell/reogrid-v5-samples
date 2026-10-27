using unvell.ReoGrid.WinForms;

namespace ReoGridSamples.LiveMarketGrid;

internal static class Program
{
	/// <summary>
	/// <c>LiveMarketGrid [--renderer d2d|gdi]</c> — the interactive demo.<br/>
	/// <c>LiveMarketGrid --bench [--rows 100000] [--ticks 200] [--background 5000] [--out file.md]</c>
	/// — run a fixed measurement on both renderers and write a Markdown report (see README).<br/>
	/// <c>LiveMarketGrid --demo [--sync]</c> — a scripted full-screen run, GDI+ then Direct2D, to record as a video.
	/// </summary>
	[STAThread]
	private static int Main(string[] args)
	{
		ApplicationConfiguration.Initialize();
		var options = Options.Parse(args);
		string license = LicenseLoader.Apply();

		if (options.Bench) return Benchmark.Run(options, license);
		if (options.Demo) return Demo.Run(options, license);

		using var form = new MainForm(options, license);
		form.Shown += (_, _) => form.SetRunning(true);
		Application.Run(form);
		return 0;
	}
}

internal sealed record Options(bool Bench, bool Demo, bool Sync, GridRenderer Renderer, int Rows, int Ticks, int Background, string? Out)
{
	public static Options Parse(string[] args)
	{
		bool bench = false, demo = false, sync = false;
		var renderer = GridRenderer.Direct2D;
		int rows = 100_000, ticks = 200, background = 5_000;
		string? output = null;
		for (int i = 0; i < args.Length; i++)
		{
			string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
			switch (args[i])
			{
				case "--bench": bench = true; break;
				case "--demo": demo = true; break;
				case "--sync": sync = true; break;
				case "--renderer":
					renderer = Next() switch
					{
						"d2d" or "direct2d" => GridRenderer.Direct2D,
						"gdi" => GridRenderer.Gdi,
						var v => throw new ArgumentException($"--renderer {v}: use d2d or gdi"),
					};
					break;
				case "--rows": rows = int.Parse(Next()); break;
				case "--ticks": ticks = int.Parse(Next()); break;
				case "--background": background = int.Parse(Next()); break;
				case "--out": output = Next(); break;
			}
		}
		return new Options(bench, demo, sync, renderer, rows, ticks, background, output);
	}
}
