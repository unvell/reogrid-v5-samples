using System.Diagnostics;
using System.Drawing.Drawing2D;
using unvell.ReoGrid.WinForms;

namespace ReoGridSamples.LiveMarketGrid;

/// <summary>
/// The band above the grid in <c>--demo</c>, sized to read in a video: the renderer that is
/// drawing, what one repaint costs, how many repaints reach the window per second, the paint time
/// of the last 30 seconds as a graph, and a caption saying what the grid is doing. What is on
/// screen is on the frame around the grid (<see cref="DemoFrame"/>).
/// </summary>
/// <remarks>
/// The first repaint after the renderer changes sets the new renderer up (device, caches). It is a
/// one-off, not what a frame costs, so it stays out of the numbers and the graph.
/// </remarks>
internal sealed class DemoHud : Control
{
	public static readonly Color Navy = Color.FromArgb(0x13, 0x26, 0x4A);
	public static readonly Color HeaderNavy = Color.FromArgb(0x1F, 0x3A, 0x68);
	public static readonly Color Rule = Color.FromArgb(0x2E, 0x4C, 0x82);
	public static readonly Color Muted = Color.FromArgb(0x9D, 0xB2, 0xD8);
	public static readonly Color Soft = Color.FromArgb(0xD5, 0xE0, 0xF2);
	public static readonly Color GdiColor = Color.FromArgb(0xF6, 0xA6, 0x23);
	public static readonly Color Direct2DColor = Color.FromArgb(0x3D, 0xDC, 0x97);

	private const int LogicalHeight = 204, MetricsHeight = 150;
	private const int SampleMs = 100;
	private const int Samples = 30_000 / SampleMs;   // 30 seconds of graph
	private const double GraphMaxMs = 25;
	private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

	private readonly TimedReoGridControl _grid;
	private readonly int _refreshHz;
	private readonly System.Windows.Forms.Timer _timer = new() { Interval = SampleMs };
	private readonly List<(double Ms, GridRenderer Renderer)> _samples = new(Samples);
	private readonly Queue<(long At, double Ms)> _recent = new();
	private double _sum;
	private int _count;
	private GridRenderer? _painting;
	private long _shownAt;
	private double _paintShown = double.NaN;
	private int _fpsShown;
	private string _title = "", _caption = "";
	private int _fontDpi;
	private Font _labelFont = null!, _valueFont = null!, _unitFont = null!, _smallFont = null!, _titleFont = null!, _captionFont = null!;

	public DemoHud(TimedReoGridControl grid, int refreshHz)
	{
		_grid = grid;
		_refreshHz = refreshHz;
		Dock = DockStyle.Top;
		Height = LogicalToDeviceUnits(LogicalHeight);
		SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
		grid.Painted += (_, _) => OnGridPainted();
		_timer.Tick += (_, _) => Sample();
		_timer.Start();
	}

	public void SetCaption(string title, string caption)
	{
		_title = title;
		_caption = caption;
		Invalidate();
	}

	/// <summary>Start the graph and the numbers afresh.</summary>
	public void Reset()
	{
		_samples.Clear();
		_recent.Clear();
		_sum = _count = 0;
		_paintShown = double.NaN;
		_fpsShown = 0;
		Invalidate();
	}

	private void OnGridPainted()
	{
		var renderer = _grid.ActiveRenderer;
		if (renderer != _painting)
		{
			_painting = renderer;
			return;
		}
		double ms = _grid.PaintTimes.Last;
		_sum += ms;
		_count++;
		_recent.Enqueue((Stopwatch.GetTimestamp(), ms));
	}

	private void Sample()
	{
		if (!Visible) return;
		if (_samples.Count == Samples) _samples.RemoveAt(0);
		_samples.Add((_count > 0 ? _sum / _count : double.NaN, _grid.ActiveRenderer));
		_sum = _count = 0;

		long now = Stopwatch.GetTimestamp();
		while (_recent.Count > 0 && Stopwatch.GetElapsedTime(_recent.Peek().At, now).TotalSeconds > 0.5) _recent.Dequeue();
		// The big numbers change four times a second: slow enough to read.
		if (Stopwatch.GetElapsedTime(_shownAt, now).TotalMilliseconds >= 250)
		{
			_shownAt = now;
			var recent = _recent.Select(r => r.Ms).Order().ToArray();
			_paintShown = recent.Length == 0 ? double.NaN : recent[recent.Length / 2];
			_fpsShown = _grid.FramesPerSecond;
		}
		Invalidate();
	}

	protected override void OnDpiChangedAfterParent(EventArgs e)
	{
		base.OnDpiChangedAfterParent(e);
		Height = LogicalToDeviceUnits(LogicalHeight);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		var g = e.Graphics;
		float s = DeviceDpi / 96f;
		int S(float logical) => (int)Math.Round(logical * s);
		EnsureFonts(s);

		g.Clear(Navy);
		int metrics = S(MetricsHeight);
		using (var band = new SolidBrush(HeaderNavy)) g.FillRectangle(band, 0, metrics, Width, Height - metrics);

		bool d2d = _grid.ActiveRenderer == GridRenderer.Direct2D;
		Color accent = d2d ? Direct2DColor : GdiColor;
		int x = S(32), labelY = S(20), valueY = S(46);

		DrawText(g, "RENDERER", _labelFont, Muted, x, labelY);
		Metric(g, d2d ? "Direct2D" : "GDI+", d2d ? "GPU" : "CPU", accent, x, valueY, S(10));
		x += S(340);
		DrawText(g, "PAINT PER FRAME", _labelFont, Muted, x, labelY);
		Metric(g, double.IsNaN(_paintShown) ? "–" : _paintShown.ToString("F1"), "ms", Color.White, x, valueY, S(8));
		x += S(280);
		DrawText(g, "FRAMES PER SECOND", _labelFont, Muted, x, labelY);
		Metric(g, _fpsShown.ToString(), _refreshHz > 0 ? $"display {_refreshHz} Hz" : "", Color.White, x, valueY, S(12));
		x += S(350);

		DrawGraph(g, new Rectangle(x, S(46), Width - x - S(32), metrics - S(46) - S(20)), s);
		DrawText(g, "PAINT TIME PER FRAME · LAST 30 SECONDS", _labelFont, Muted, x, labelY);

		// Caption row: round title, then what is happening.
		int rowH = Height - metrics;
		int titleY = metrics + (rowH - _titleFont.Height) / 2;
		int captionX = S(32);
		if (_title != "")
			captionX += TextWidth(g, _title, _titleFont) + S(22);
		DrawText(g, _title, _titleFont, Color.White, S(32), titleY);
		DrawText(g, _caption, _captionFont, Soft, captionX, titleY + Ascent(_titleFont) - Ascent(_captionFont));
	}

	/// <param name="area">The graph with its labels; the labels of the frame lines take a gutter on the left.</param>
	private void DrawGraph(Graphics g, Rectangle area, float s)
	{
		int gutter = (int)(104 * s);
		var r = new Rectangle(area.Left + gutter, area.Top, area.Width - gutter, area.Height);
		if (r.Width <= 0 || r.Height <= 0) return;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		float step = r.Width / (float)(Samples - 1);
		int n = _samples.Count;
		float X(int i) => r.Right - (n - 1 - i) * step;
		float Y(double ms) => r.Bottom - (float)(Math.Min(ms, GraphMaxMs) / GraphMaxMs * r.Height);

		using (var axis = new Pen(Rule, s)) g.DrawLine(axis, r.Left, r.Bottom, r.Right, r.Bottom);

		// What one frame of the display allows: the paint has to fit in it, with the rest of the tick.
		using (var budget = new Pen(Color.FromArgb(0x5D, 0x7A, 0xB0), s) { DashStyle = DashStyle.Dash })
			foreach (int hz in new[] { _refreshHz, 60 }.Where(h => h > 0).Distinct())
			{
				float y = Y(1000.0 / hz);
				g.DrawLine(budget, r.Left, y, r.Right, y);
				string label = $"{hz} Hz frame";
				DrawText(g, label, _smallFont, Muted, r.Left - (int)(10 * s) - TextWidth(g, label, _smallFont), (int)y - _smallFont.Height / 2);
			}

		for (int i = 0; i < n;)
		{
			if (double.IsNaN(_samples[i].Ms)) { i++; continue; }
			var renderer = _samples[i].Renderer;
			int j = i;
			while (j + 1 < n && !double.IsNaN(_samples[j + 1].Ms) && _samples[j + 1].Renderer == renderer) j++;

			var line = new PointF[j - i + 1];
			for (int k = 0; k < line.Length; k++) line[k] = new PointF(X(i + k), Y(_samples[i + k].Ms));
			Color c = renderer == GridRenderer.Direct2D ? Direct2DColor : GdiColor;
			using (var fill = new SolidBrush(Color.FromArgb(60, c)))
				g.FillPolygon(fill, [.. line, new PointF(X(j), r.Bottom), new PointF(X(i), r.Bottom)]);
			if (line.Length > 1)
				using (var pen = new Pen(c, 2.5f * s) { LineJoin = LineJoin.Round })
					g.DrawLines(pen, line);

			// Mark where the renderer changed.
			if (i > 0 && _samples[i - 1].Renderer != renderer)
			{
				float mx = X(i) - step / 2;
				using (var mark = new Pen(c, 1.5f * s) { DashStyle = DashStyle.Dot }) g.DrawLine(mark, mx, r.Top, mx, r.Bottom);
				DrawText(g, renderer == GridRenderer.Direct2D ? "→ Direct2D" : "→ GDI+", _smallFont, c, (int)(mx + 6 * s), r.Top);
			}
			i = j + 1;
		}
		g.SmoothingMode = SmoothingMode.None;
	}

	/// <summary>A big value with a small unit after it, on the same baseline.</summary>
	private void Metric(Graphics g, string value, string unit, Color color, int x, int y, int gap)
	{
		DrawText(g, value, _valueFont, color, x, y);
		if (unit == "") return;
		DrawText(g, unit, _unitFont, Muted, x + TextWidth(g, value, _valueFont) + gap, y + Ascent(_valueFont) - Ascent(_unitFont));
	}

	private static void DrawText(Graphics g, string text, Font font, Color color, int x, int y) =>
		TextRenderer.DrawText(g, text, font, new Point(x, y), color, Flags);

	private static int TextWidth(Graphics g, string text, Font font) =>
		text == "" ? 0 : TextRenderer.MeasureText(g, text, font, Size.Empty, Flags).Width;

	private static int Ascent(Font font) =>
		(int)Math.Round(font.Size * font.FontFamily.GetCellAscent(font.Style) / font.FontFamily.GetEmHeight(font.Style));

	private void EnsureFonts(float s)
	{
		if (_fontDpi == DeviceDpi) return;
		DisposeFonts();
		_fontDpi = DeviceDpi;
		_labelFont = new Font("Segoe UI Semibold", 15 * s, GraphicsUnit.Pixel);
		_valueFont = new Font("Segoe UI Semibold", 54 * s, GraphicsUnit.Pixel);
		_unitFont = new Font("Segoe UI", 22 * s, GraphicsUnit.Pixel);
		_smallFont = new Font("Segoe UI", 16 * s, GraphicsUnit.Pixel);
		_titleFont = new Font("Segoe UI Semibold", 26 * s, GraphicsUnit.Pixel);
		_captionFont = new Font("Segoe UI", 26 * s, GraphicsUnit.Pixel);
	}

	private void DisposeFonts()
	{
		if (_fontDpi == 0) return;
		foreach (var f in new[] { _labelFont, _valueFont, _unitFont, _smallFont, _titleFont, _captionFont }) f.Dispose();
		_fontDpi = 0;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_timer.Dispose();
			DisposeFonts();
		}
		base.Dispose(disposing);
	}
}
