using System.Diagnostics;
using unvell.ReoGrid.Core;
using unvell.ReoGrid.Core.Style;

namespace ReoGridSamples.LiveMarketGrid;

/// <summary>Which cells a tick changes.</summary>
internal enum UpdatePattern
{
	/// <summary>~85% of the visible instruments move, 15 fields each, plus
	/// <see cref="MarketFeed.BackgroundPerSheet"/> instruments on every sheet, on or off screen.</summary>
	Market,

	/// <summary>The "Updated" column of every visible instrument gets the current time (text,
	/// <c>HH:mm:ss.fff</c>) and 8 visible instruments get a new last price — about 108 cells on a
	/// 100-row window: a blotter whose clock column ticks while a few prices move.</summary>
	ClockAndFew,

	/// <summary>All 15 changing fields of every visible instrument.</summary>
	EveryVisible,

	/// <summary>Nothing is written; the grid is only asked to repaint.</summary>
	Nothing,
}

/// <summary>What one tick changed, and what it cost on the application side.</summary>
internal readonly record struct TickResult(int VisibleRows, int BackgroundRows, int CellsWritten, int FlashedCells, double UpdateMs);

/// <summary>
/// The simulated market-data feed. The feed only writes to the model. It never touches the
/// control — the caller asks for one repaint afterwards, which is the whole trick to keeping a
/// ticking grid cheap.
/// </summary>
internal sealed class MarketFeed
{
	private const int FieldsPerInstrument = 15;
	private const int FewPrices = 8;

	private static readonly StyleRecord Up = new() { BackgroundColor = 0xFFD7F5E3, Color = 0xFF0B6B2E };
	private static readonly StyleRecord Down = new() { BackgroundColor = 0xFFFDE2E1, Color = 0xFFB42318 };
	private static readonly int[] FlashColumns = [MarketSheet.ColBid, MarketSheet.ColAsk, MarketSheet.ColLast];

	private readonly MarketSheet[] _sheets;
	private readonly Random _rng = new(7);
	private readonly List<(Worksheet Sheet, int Row, int Col)> _flashed = new();
	private readonly List<int> _visible = new();

	public MarketFeed(MarketSheet[] sheets) => _sheets = sheets;

	public UpdatePattern Pattern { get; set; } = UpdatePattern.Market;

	/// <summary>Share of the visible instruments that move every tick in the <see cref="UpdatePattern.Market"/> pattern.</summary>
	public double VisibleShare { get; set; } = 0.85;

	/// <summary>Instruments moved per sheet per tick anywhere in the sheet, including off screen
	/// and on the sheets that are not shown (<see cref="UpdatePattern.Market"/> only).</summary>
	public int BackgroundPerSheet { get; set; } = 5_000;

	/// <summary>Flash the price cells that moved on screen until the next tick.</summary>
	public bool Flash { get; set; } = true;

	/// <param name="active">The sheet on screen.</param>
	/// <param name="firstRow">First visible (scrolling) row of the active sheet.</param>
	/// <param name="lastRow">Last visible row of the active sheet.</param>
	public TickResult Tick(MarketSheet active, int firstRow, int lastRow)
	{
		long t0 = Stopwatch.GetTimestamp();
		double now = DateTime.Now.ToOADate();

		// The previous tick's flash ends. Only on-screen cells were ever flashed, so this list
		// stays at a few hundred entries however large the sheets are.
		foreach (var (sheet, row, col) in _flashed) sheet.SetCellStyle(row, col, null);
		_flashed.Clear();

		_visible.Clear();
		for (int r = Math.Max(firstRow, active.FirstDataRow), r1 = Math.Min(lastRow, active.LastDataRow); r <= r1; r++)
			if (active.IsInstrumentRow(r)) _visible.Add(r);

		int visible = 0, background = 0, cells = 0;
		switch (Pattern)
		{
			case UpdatePattern.Market:
			case UpdatePattern.EveryVisible:
				double share = Pattern == UpdatePattern.Market ? VisibleShare : 1;
				foreach (int r in _visible)
				{
					if (share < 1 && _rng.NextDouble() >= share) continue;
					FlashPrices(active, r, active.Tick(r, now));
					visible++;
				}
				cells = visible * FieldsPerInstrument;
				if (Pattern == UpdatePattern.EveryVisible) break;

				foreach (var s in _sheets)
					for (int i = 0; i < BackgroundPerSheet; i++)
					{
						int r = s.FirstDataRow + _rng.Next(s.DataRows);
						if (!s.IsInstrumentRow(r)) continue;
						s.Tick(r, now);
						background++;
					}
				cells += background * FieldsPerInstrument;
				break;

			case UpdatePattern.ClockAndFew:
				string clock = DateTime.Now.ToString("HH:mm:ss.fff");
				foreach (int r in _visible) active.Sheet.SetText(r, MarketSheet.ColTime, clock);
				for (int i = 0; i < FewPrices && _visible.Count > 0; i++)
				{
					int r = _visible[_rng.Next(_visible.Count)];
					int dir = active.TickLast(r);
					if (Flash && dir != 0) FlashCell(active.Sheet, r, MarketSheet.ColLast, dir);
				}
				visible = _visible.Count;
				cells = _visible.Count + Math.Min(FewPrices, _visible.Count);
				break;
		}

		double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
		return new TickResult(visible, background, cells, _flashed.Count, ms);
	}

	private void FlashPrices(MarketSheet sheet, int row, int dir)
	{
		if (!Flash || dir == 0) return;
		foreach (int c in FlashColumns) FlashCell(sheet.Sheet, row, c, dir);
	}

	private void FlashCell(Worksheet sheet, int row, int col, int dir)
	{
		sheet.SetCellStyle(row, col, dir > 0 ? Up : Down);
		_flashed.Add((sheet, row, col));
	}
}
