using unvell.ReoGrid.WinForms;

namespace ReoGridSamples.LiveMarketGrid;

/// <summary>
/// The frame around the grid in <c>--demo</c>: a border and a title strip in the colour of the
/// renderer that is drawing, so that the running sample stands apart from the band that explains it.
/// The strip names the sample and what it shows.
/// </summary>
/// <remarks>A <see cref="Panel"/>, not a plain <see cref="Control"/>: docking leaves a panel's
/// <see cref="Control.Padding"/> clear, which is where the frame is drawn.</remarks>
internal sealed class DemoFrame : Panel
{
	private const int LogicalMargin = 24, LogicalGap = 18, LogicalBorder = 3, LogicalStrip = 34;
	private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix
		| TextFormatFlags.VerticalCenter | TextFormatFlags.NoClipping;

	private readonly TimedReoGridControl _grid;
	private GridRenderer _renderer;
	private string _info = "";
	private int _fontDpi;
	private Font _nameFont = null!, _infoFont = null!;

	public DemoFrame(TimedReoGridControl grid)
	{
		_grid = grid;
		_renderer = grid.ActiveRenderer;
		Dock = DockStyle.Fill;
		SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
		UpdatePadding();
		Controls.Add(grid);
		grid.Painted += (_, _) =>
		{
			if (grid.ActiveRenderer == _renderer) return;
			_renderer = grid.ActiveRenderer;
			Invalidate();
		};
	}

	/// <summary>What the strip says after the sample's name.</summary>
	public void SetInfo(string info)
	{
		_info = info;
		Invalidate();
	}

	protected override void OnDpiChangedAfterParent(EventArgs e)
	{
		base.OnDpiChangedAfterParent(e);
		UpdatePadding();
	}

	private void UpdatePadding()
	{
		int margin = LogicalToDeviceUnits(LogicalMargin), border = LogicalToDeviceUnits(LogicalBorder);
		Padding = new Padding(margin + border, LogicalToDeviceUnits(LogicalGap + LogicalStrip), margin + border, margin + border);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		var g = e.Graphics;
		float s = DeviceDpi / 96f;
		int S(float logical) => (int)Math.Round(logical * s);
		EnsureFonts(s);

		g.Clear(DemoHud.Navy);
		bool d2d = _renderer == GridRenderer.Direct2D;
		Color accent = d2d ? DemoHud.Direct2DColor : DemoHud.GdiColor;
		var grid = _grid.Bounds;
		int border = S(LogicalBorder);
		var strip = new Rectangle(grid.Left - border, grid.Top - S(LogicalStrip), grid.Width + border * 2, S(LogicalStrip));
		using (var brush = new SolidBrush(accent))
		{
			g.FillRectangle(brush, strip);
			g.FillRectangle(brush, grid.Left - border, grid.Top, border, grid.Height + border);
			g.FillRectangle(brush, grid.Right, grid.Top, border, grid.Height + border);
			g.FillRectangle(brush, grid.Left, grid.Bottom, grid.Width, border);
		}

		// The sample's name and what it shows on the left, the renderer on the right.
		int pad = S(14);
		string renderer = d2d ? "Renderer: Direct2D (GPU)" : "Renderer: GDI+ (CPU)";
		int rendererW = TextRenderer.MeasureText(g, renderer, _nameFont, Size.Empty, Flags).Width;
		TextRenderer.DrawText(g, renderer, _nameFont, new Rectangle(strip.Right - pad - rendererW, strip.Top, rendererW, strip.Height), DemoHud.Navy, Flags);
		const string name = "LiveMarketGrid";
		int nameW = TextRenderer.MeasureText(g, name, _nameFont, Size.Empty, Flags).Width;
		TextRenderer.DrawText(g, name, _nameFont, new Rectangle(strip.Left + pad, strip.Top, nameW, strip.Height), DemoHud.Navy, Flags);
		int infoX = strip.Left + pad + nameW + S(16);
		TextRenderer.DrawText(g, _info, _infoFont, new Rectangle(infoX, strip.Top, strip.Right - pad - rendererW - S(24) - infoX, strip.Height), DemoHud.Navy,
			Flags | TextFormatFlags.EndEllipsis);
	}

	private void EnsureFonts(float s)
	{
		if (_fontDpi == DeviceDpi) return;
		DisposeFonts();
		_fontDpi = DeviceDpi;
		_nameFont = new Font("Segoe UI Semibold", 17 * s, GraphicsUnit.Pixel);
		_infoFont = new Font("Segoe UI", 17 * s, GraphicsUnit.Pixel);
	}

	private void DisposeFonts()
	{
		if (_fontDpi == 0) return;
		_nameFont.Dispose();
		_infoFont.Dispose();
		_fontDpi = 0;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing) DisposeFonts();
		base.Dispose(disposing);
	}
}
