using System.Diagnostics;
using unvell.ReoGrid.WinForms;

namespace ReoGridSamples.LiveMarketGrid;

/// <summary>
/// <see cref="ReoGridControl"/> with a stopwatch around <see cref="OnPaint"/>, so the demo can
/// show what one repaint actually costs, and a count of repaints per second. Measuring inside
/// OnPaint — not around the code that requested the repaint — is what separates paint time from
/// everything else on the UI thread.
/// </summary>
/// <remarks>
/// With Direct2D the measured time includes presenting the frame to the window. With GDI+ the
/// control paints into a back buffer, and WinForms copies it to the window after OnPaint returns.
/// </remarks>
internal sealed class TimedReoGridControl : ReoGridControl
{
	private readonly Queue<long> _recent = new();

	public PaintStats PaintTimes { get; } = new(capacity: 240);

	/// <summary>Raised after every repaint.</summary>
	public event EventHandler? Painted;

	protected override void OnPaint(PaintEventArgs e)
	{
		long t0 = Stopwatch.GetTimestamp();
		base.OnPaint(e);
		long t1 = Stopwatch.GetTimestamp();
		PaintTimes.Add(Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds);
		_recent.Enqueue(t1);
		Trim(t1);
		Painted?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>Repaints in the last second.</summary>
	public int FramesPerSecond
	{
		get
		{
			Trim(Stopwatch.GetTimestamp());
			return _recent.Count;
		}
	}

	private void Trim(long now)
	{
		while (_recent.Count > 0 && Stopwatch.GetElapsedTime(_recent.Peek(), now).TotalSeconds > 1) _recent.Dequeue();
	}

	/// <summary>Rows of the scrolling area currently on screen (frozen header rows excluded).</summary>
	public (int First, int Last) VisibleRows()
	{
		double scale = DpiScale * Zoom;
		double height = (ClientSize.Height - SystemInformation.HorizontalScrollBarHeight) / scale;
		return Viewport.VisibleRows(height);
	}
}

/// <summary>Rolling window of the most recent measurements.</summary>
internal sealed class PaintStats
{
	private readonly double[] _values;
	private int _next, _count;

	public PaintStats(int capacity) => _values = new double[capacity];

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

	public double Average => _count == 0 ? 0 : _values.AsSpan(0, _count).ToArray().Average();

	public double Max => _count == 0 ? 0 : _values.AsSpan(0, _count).ToArray().Max();

	public double Percentile(double p)
	{
		if (_count == 0) return 0;
		var sorted = _values.AsSpan(0, _count).ToArray();
		Array.Sort(sorted);
		return sorted[Math.Clamp((int)Math.Ceiling(p / 100 * _count) - 1, 0, _count - 1)];
	}

	public double[] Snapshot() => _values.AsSpan(0, _count).ToArray();
}
