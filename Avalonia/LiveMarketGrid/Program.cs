using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace ReoGridSamples.LiveMarketGrid;

internal static class Program
{
	/// <summary>
	/// <c>LiveMarketGrid</c> — the interactive demo.<br/>
	/// <c>LiveMarketGrid --bench [--rows 100000] [--frames 300] [--background 5000] [--out file.md]</c>
	/// — run a fixed measurement and write a Markdown report (see README).
	/// </summary>
	[STAThread]
	private static int Main(string[] args)
	{
		App.Options = Options.Parse(args);
		App.LicenseState = LicenseLoader.Apply();
		return AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
	}
}

internal sealed class App : Application
{
	public static Options Options { get; set; } = Options.Parse([]);
	public static string LicenseState { get; set; } = "";

	public override void Initialize() => Styles.Add(new FluentTheme());

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			var window = new MainWindow(Options, LicenseState);
			desktop.MainWindow = window;
			if (Options.Bench)
				window.Opened += async (_, _) => desktop.Shutdown(await Benchmark.RunAsync(window, Options));
			else
				window.Opened += (_, _) => window.SetRunning(true);
		}
		base.OnFrameworkInitializationCompleted();
	}
}

internal sealed record Options(bool Bench, int Rows, int Frames, int Background, string? Out)
{
	public static Options Parse(string[] args)
	{
		bool bench = false;
		int rows = 100_000, frames = 300, background = 5_000;
		string? output = null;
		for (int i = 0; i < args.Length; i++)
		{
			string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
			switch (args[i])
			{
				case "--bench": bench = true; break;
				case "--rows": rows = int.Parse(Next()); break;
				case "--frames": frames = int.Parse(Next()); break;
				case "--background": background = int.Parse(Next()); break;
				case "--out": output = Next(); break;
			}
		}
		return new Options(bench, rows, frames, background, output);
	}
}
