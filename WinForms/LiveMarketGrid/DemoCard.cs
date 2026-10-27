using System.Diagnostics;

namespace ReoGridSamples.LiveMarketGrid;

/// <summary>What a <see cref="DemoCard"/> shows. Every part is optional.</summary>
internal sealed record DemoCardContent
{
	public string Kicker { get; init; } = "";
	public string Title { get; init; } = "";
	/// <summary>Paragraphs, after the code if there is any.</summary>
	public string[] Lines { get; init; } = [];

	/// <summary>Code lines; whatever follows <c>//</c> is drawn as a comment.</summary>
	public string[] Code { get; init; } = [];

	/// <summary>Rows of cells, the first row being the header.</summary>
	public string[][] Table { get; init; } = [];

	/// <summary>Text colour of each table column (white where not given).</summary>
	public Color[] TableColors { get; init; } = [];

	public string[] Footer { get; init; } = [];
}

/// <summary>
/// A full-window card in <c>--demo</c> — the title, the code, the results. The parts that are set
/// are stacked and centred in the window. A thin bar along the bottom fills up over the time the
/// card stays; it also keeps the window changing, which a window recorder needs to deliver frames.
/// </summary>
internal sealed class DemoCard : Control
{
	private const TextFormatFlags Wrap = TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

	private readonly DemoCardContent _content;
	private readonly double _seconds;
	private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
	private long _start;

	public DemoCard(DemoCardContent content, double seconds)
	{
		_content = content;
		_seconds = seconds;
		Dock = DockStyle.Fill;
		SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
		_timer.Tick += (_, _) => Invalidate(BarBounds);
	}

	/// <summary>Starts the bar.</summary>
	public void Start()
	{
		_start = Stopwatch.GetTimestamp();
		_timer.Start();
	}

	private Rectangle BarBounds => new(0, ClientSize.Height - LogicalToDeviceUnits(6), ClientSize.Width, LogicalToDeviceUnits(6));

	protected override void OnPaint(PaintEventArgs e)
	{
		var g = e.Graphics;
		var bar = BarBounds;
		double progress = _start == 0 ? 0 : Math.Min(1, Stopwatch.GetElapsedTime(_start).TotalSeconds / _seconds);
		using (var track = new SolidBrush(DemoHud.HeaderNavy)) g.FillRectangle(track, bar);
		using (var fill = new SolidBrush(DemoHud.Direct2DColor)) g.FillRectangle(fill, bar.X, bar.Y, (int)(bar.Width * progress), bar.Height);
		if (e.ClipRectangle.Top >= bar.Top) return;   // only the bar moved

		float s = DeviceDpi / 96f;
		using (var background = new SolidBrush(DemoHud.Navy)) g.FillRectangle(background, 0, 0, ClientSize.Width, bar.Top);
		using var fonts = new Fonts(s);
		int width = Math.Min(ClientSize.Width - (int)(160 * s), (int)(1500 * s));
		int left = (ClientSize.Width - width) / 2;
		int height = Flow(g, fonts, left, 0, width, s, draw: false);
		Flow(g, fonts, left, Math.Max(0, (ClientSize.Height - height) / 2), width, s, draw: true);
	}

	/// <summary>Lays the parts out from <paramref name="top"/> down, drawing them if asked; returns the height.</summary>
	private int Flow(Graphics g, Fonts f, int left, int top, int width, float s, bool draw)
	{
		int S(float logical) => (int)Math.Round(logical * s);
		var (kicker, title, lines, code, table, colors, footer) =
			(_content.Kicker, _content.Title, _content.Lines, _content.Code, _content.Table, _content.TableColors, _content.Footer);
		int y = top;

		int Block(string text, Font font, Color color, int gapAfter)
		{
			int h = TextRenderer.MeasureText(g, text, font, new Size(width, int.MaxValue), Wrap).Height;
			if (draw) TextRenderer.DrawText(g, text, font, new Rectangle(left, y, width, h), color, Wrap);
			return h + gapAfter;
		}

		if (kicker != "") y += Block(kicker.ToUpperInvariant(), f.Kicker, DemoHud.Direct2DColor, S(16));
		if (title != "") y += Block(title, f.Title, Color.White, S(30));

		if (code.Length > 0)
		{
			int pad = S(30), lineH = (int)(f.Code.Height * 1.35);
			int boxH = pad * 2 + lineH * code.Length - (lineH - f.Code.Height);
			if (draw)
			{
				using (var box = new SolidBrush(Color.FromArgb(0x0B, 0x18, 0x31))) g.FillRectangle(box, left, y, width, boxH);
				using (var border = new Pen(DemoHud.Rule, s)) g.DrawRectangle(border, left, y, width - 1, boxH - 1);
				for (int i = 0; i < code.Length; i++)
				{
					string line = code[i];
					int comment = line.IndexOf("//", StringComparison.Ordinal);
					string text = comment < 0 ? line : line[..comment];
					var at = new Point(left + pad, y + pad + i * lineH);
					TextRenderer.DrawText(g, text, f.Code, at, Color.FromArgb(0xE6, 0xED, 0xF7), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
					if (comment >= 0)
					{
						int w = TextRenderer.MeasureText(g, text, f.Code, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
						TextRenderer.DrawText(g, line[comment..], f.Code, new Point(at.X + w, at.Y), DemoHud.Muted, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
					}
				}
			}
			y += boxH + S(30);
		}

		foreach (string line in lines) y += Block(line, f.Body, DemoHud.Soft, S(14));

		if (table.Length > 0)
		{
			y += S(10);
			int columns = table[0].Length;
			int first = (int)(width * 0.36), other = (width - first) / Math.Max(1, columns - 1);
			for (int row = 0; row < table.Length; row++)
			{
				bool header = row == 0;
				int rowH = header ? S(44) : S(76);
				for (int col = 0; col < columns && draw; col++)
				{
					var font = header ? f.TableHeader : col == 0 ? f.Body : f.TableValue;
					Color color = header ? DemoHud.Muted : col < colors.Length ? colors[col] : Color.White;
					var cell = col == 0 ? new Rectangle(left, y, first, rowH) : new Rectangle(left + first + (col - 1) * other, y, other, rowH);
					var align = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter
						| (col == 0 ? TextFormatFlags.Left : TextFormatFlags.Right);
					TextRenderer.DrawText(g, table[row][col], font, cell, color, align);
				}
				y += rowH;
				if (draw)
					using (var rule = new Pen(DemoHud.Rule, s)) g.DrawLine(rule, left, y, left + width, y);
			}
			y += S(30);
		}

		foreach (string line in footer) y += Block(line, f.Footer, DemoHud.Muted, S(8));
		return y - top;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing) _timer.Dispose();
		base.Dispose(disposing);
	}

	private sealed class Fonts(float s) : IDisposable
	{
		public readonly Font Kicker = new("Segoe UI Semibold", 23 * s, GraphicsUnit.Pixel);
		public readonly Font Title = new("Segoe UI Semibold", 64 * s, GraphicsUnit.Pixel);
		public readonly Font Body = new("Segoe UI", 29 * s, GraphicsUnit.Pixel);
		public readonly Font Code = new("Consolas", 30 * s, GraphicsUnit.Pixel);
		public readonly Font TableHeader = new("Segoe UI Semibold", 20 * s, GraphicsUnit.Pixel);
		public readonly Font TableValue = new("Segoe UI Semibold", 32 * s, GraphicsUnit.Pixel);
		public readonly Font Footer = new("Segoe UI", 19 * s, GraphicsUnit.Pixel);

		public void Dispose()
		{
			foreach (var f in new[] { Kicker, Title, Body, Code, TableHeader, TableValue, Footer }) f.Dispose();
		}
	}
}
