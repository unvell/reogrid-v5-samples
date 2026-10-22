using System.Diagnostics;
using Avalonia.Media;
using unvell.ReoGrid.Avalonia;

namespace ReoGridSamples.LiveMarketGrid;

/// <summary>
/// <see cref="ReoGridControl"/> with a stopwatch around <see cref="Render"/>, so the demo can show
/// what one repaint costs on the UI thread, and a count of how many repaints reach the screen.
/// </summary>
/// <remarks>
/// On Avalonia, <c>Render</c> lays the visible cells out and records the drawing; a separate render
/// thread rasterizes it. The frame rate covers both, so watch it alongside the render time.
/// </remarks>
internal sealed class TimedReoGridControl : ReoGridControl
{
	private readonly Queue<long> _recent = new();

	public RollingStats RenderTimes { get; } = new(capacity: 2_000);

	public override void Render(DrawingContext context)
	{
		long t0 = Stopwatch.GetTimestamp();
		base.Render(context);
		long t1 = Stopwatch.GetTimestamp();
		RenderTimes.Add(Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds);
		_recent.Enqueue(t1);
	}

	/// <summary>Repaints in the last second.</summary>
	public int FramesPerSecond
	{
		get
		{
			long now = Stopwatch.GetTimestamp();
			while (_recent.Count > 0 && Stopwatch.GetElapsedTime(_recent.Peek(), now).TotalSeconds > 1) _recent.Dequeue();
			return _recent.Count;
		}
	}

	/// <summary>Rows of the scrolling area currently on screen (frozen header rows excluded).</summary>
	public (int First, int Last) VisibleRows() => Viewport.VisibleRows(Bounds.Height / Zoom);
}

/// <summary>Rolling window of the most recent measurements.</summary>
internal sealed class RollingStats
{
	private readonly double[] _values;
	private int _next, _count;

	public RollingStats(int capacity) => _values = new double[capacity];

	public long Total { get; private set; }
	public double Last { get; private set; }

	public void Add(double ms)
	{
		_values[_next] = ms;
		_next = (_next + 1) % _values.Length;
		if (_count < _values.Length) _count++;
		Last = ms;
		Total++;
	}

	public void Clear() { _next = _count = 0; Last = 0; Total = 0; }

	public double Average => _count == 0 ? 0 : Snapshot().Average();

	public double Percentile(double p)
	{
		if (_count == 0) return 0;
		var sorted = Snapshot();
		Array.Sort(sorted);
		return sorted[Math.Clamp((int)Math.Ceiling(p / 100 * _count) - 1, 0, _count - 1)];
	}

	public double[] Snapshot() => _values.AsSpan(0, _count).ToArray();
}
