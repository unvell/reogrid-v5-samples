using unvell.ReoGrid.Core;
using unvell.ReoGrid.Core.CellTypes;
using unvell.ReoGrid.Core.Data;
using unvell.ReoGrid.Core.Style;

namespace ReoGridSamples.LiveMarketGrid;

/// <summary>
/// One market-data sheet: a two-row grouped header, then instruments in blocks of
/// <see cref="GroupSize"/> rows, each block opened by a merged group row (sector / currency /
/// underlying). Holds the per-instrument state the simulated feed moves.
///
/// <para>Everything that applies to a whole column — fonts, alignment, widths — is set once on
/// the column (<see cref="Worksheet.SetColumnStyle"/>), and every per-row decoration on the row
/// (<see cref="Worksheet.SetRowStyle"/>). Only values, number formats and the short-lived flash
/// colours live on cells. That keeps the style table at a few dozen entries however many rows
/// the sheet has.</para>
/// </summary>
internal sealed class MarketSheet
{
	public const int HeaderRows = 2;
	public const int GroupSize = 25;          // 1 group row + 24 instruments
	public const int ColumnCount = 20;

	public const int ColWatch = 0, ColSymbol = 1, ColName = 2, ColVenue = 3;
	public const int ColBid = 4, ColAsk = 5, ColBidSize = 6, ColAskSize = 7, ColSpread = 8;
	public const int ColLast = 9, ColChange = 10, ColChangePct = 11, ColVolume = 12;
	public const int ColOpen = 13, ColHigh = 14, ColLow = 15, ColVwap = 16, ColTime = 17;
	public const int ColPnl = 18, ColFill = 19;

	public Worksheet Sheet { get; }
	public string Title { get; }
	public int DataRows { get; }
	public int InstrumentCount { get; }
	public int FirstDataRow => HeaderRows;
	public int LastDataRow => HeaderRows + DataRows - 1;

	private readonly double _tickSize;
	private readonly double[] _last, _open, _high, _low, _vwap, _volume, _position;
	private readonly Random _rng;

	public static bool IsGroupRow(int row) => row >= HeaderRows && (row - HeaderRows) % GroupSize == 0;

	public bool IsInstrumentRow(int row) => row > HeaderRows && row <= LastDataRow && !IsGroupRow(row);

	private MarketSheet(Worksheet sheet, string title, int dataRows, double tickSize, int seed)
	{
		Sheet = sheet;
		Title = title;
		DataRows = dataRows;
		_tickSize = tickSize;
		_rng = new Random(seed);
		int n = HeaderRows + dataRows;
		_last = new double[n]; _open = new double[n]; _high = new double[n]; _low = new double[n];
		_vwap = new double[n]; _volume = new double[n]; _position = new double[n];
		InstrumentCount = dataRows - (dataRows + GroupSize - 1) / GroupSize;
	}

	// ---- the three sheets of the demo ---------------------------------------------------

	public static MarketSheet[] CreateDemoSheets(Workbook wb, int dataRows) =>
	[
		Create(wb, new SheetSpec("Equities", "EQ", "#,##0.00", 0.01, 20, 4_000,
			["Technology", "Financials", "Health Care", "Industrials", "Energy", "Consumer", "Materials", "Utilities"],
			["TSE", "NYSE", "NASDAQ", "LSE", "HKEX"]), dataRows, seed: 1),
		Create(wb, new SheetSpec("FX", "FX", "0.0000", 0.0001, 0.6, 180,
			["USD crosses", "EUR crosses", "JPY crosses", "GBP crosses", "Emerging", "Scandies"],
			["EBS", "Reuters", "LMAX", "Currenex"]), dataRows, seed: 2),
		Create(wb, new SheetSpec("Futures", "FUT", "#,##0.0", 0.1, 50, 40_000,
			["Equity index", "Rates", "Energy", "Metals", "Agriculture", "Volatility"],
			["CME", "ICE", "EUREX", "OSE", "SGX"]), dataRows, seed: 3),
	];

	private sealed record SheetSpec(string Title, string Prefix, string PriceFormat, double TickSize,
		double MinPrice, double MaxPrice, string[] Groups, string[] Venues);

	private static MarketSheet Create(Workbook wb, SheetSpec spec, int dataRows, int seed)
	{
		var ws = wb.AddWorksheet(spec.Title, HeaderRows + dataRows, ColumnCount);
		var m = new MarketSheet(ws, spec.Title, dataRows, spec.TickSize, seed);
		m.Layout(spec);
		m.Fill(spec);
		return m;
	}

	// ---- layout -------------------------------------------------------------------------

	private static readonly (string Text, double Width)[] Fields =
	[
		("Watch", 46), ("Symbol", 84), ("Name", 170), ("Venue", 74),
		("Bid", 84), ("Ask", 84), ("Bid size", 72), ("Ask size", 72), ("Spread", 64),
		("Last", 88), ("Chg", 74), ("Chg %", 66), ("Volume", 96),
		("Open", 84), ("High", 84), ("Low", 84), ("VWAP", 84), ("Updated", 72),
		("P&L", 96), ("Fill", 90),
	];

	private static readonly (string Text, int Col, int Cols)[] Groups =
	[
		("Quote", ColBid, 5), ("Trade", ColLast, 4), ("Session", ColOpen, 5), ("Position", ColPnl, 2),
	];

	private void Layout(SheetSpec spec)
	{
		var ws = Sheet;
		ws.RootStyle = ws.RootStyle with { FontFamily = "Segoe UI", FontSize = 9f, VerticalAlign = VAlign.Middle };
		ws.View.FrozenRows = HeaderRows;
		ws.View.FrozenCols = ColVenue;   // Watch / Symbol / Name stay put while scrolling right

		for (int c = 0; c < ColumnCount; c++) ws.Columns.SetSize(c, Fields[c].Width);

		// Column-wide fonts: one style entry per column, not one per cell.
		var numbers = new StyleRecord { FontFamily = "Consolas", TextAlign = HAlign.Right };
		for (int c = ColBid; c <= ColPnl; c++) ws.SetColumnStyle(c, numbers);
		ws.SetColumnStyle(ColLast, numbers with { Bold = true });
		ws.SetColumnStyle(ColTime, numbers with { Color = 0xFF6B7280 });
		ws.SetColumnStyle(ColSymbol, new StyleRecord { FontFamily = "Segoe UI Semibold", FontSize = 9.5f, Color = 0xFF1F3A68 });
		ws.SetColumnStyle(ColName, new StyleRecord { FontFamily = "Arial", FontSize = 9f });
		ws.SetColumnStyle(ColVenue, new StyleRecord { FontFamily = "Segoe UI", FontSize = 8.5f, TextAlign = HAlign.Center, Color = 0xFF4B5563 });

		// Two-row header: the instrument columns span both rows, the market-data fields are grouped.
		var groupHead = new StyleRecord
		{
			FontFamily = "Segoe UI Semibold", FontSize = 10f, Bold = true, TextAlign = HAlign.Center,
			Color = 0xFFFFFFFF, BackgroundColor = 0xFF1F3A68,
		};
		ws.SetRowStyle(0, groupHead);
		ws.SetRowStyle(1, groupHead with { FontSize = 9f, BackgroundColor = 0xFF2F5597 });
		for (int c = ColWatch; c <= ColVenue; c++)
		{
			ws.SetText(0, c, Fields[c].Text);
			ws.MergeRange(new RangePosition(0, c, 2, 1));
		}
		foreach (var (text, col, cols) in Groups)
		{
			ws.SetText(0, col, text);
			ws.MergeRange(new RangePosition(0, col, 1, cols));
		}
		for (int c = ColBid; c < ColumnCount; c++) ws.SetText(1, c, Fields[c].Text);

		// Number formats per column over the data rows.
		string px = spec.PriceFormat;
		SetFormat(px, ColBid, ColAsk, ColLast, ColOpen, ColHigh, ColLow, ColVwap);
		SetFormat("#,##0", ColBidSize, ColAskSize, ColVolume);
		SetFormat(px, ColSpread);
		SetFormat("+" + px + ";[Red]-" + px + ";" + px, ColChange);
		SetFormat("+0.00%;[Red]-0.00%;0.00%", ColChangePct);
		SetFormat("hh:mm:ss", ColTime);
		SetFormat("#,##0;[Red]-#,##0", ColPnl);

		// Group rows: one merged row per block, decorated on the row.
		var groupRow = new StyleRecord
		{
			FontFamily = "Segoe UI Semibold", FontSize = 9.5f, Bold = true,
			Color = 0xFF1F3A68, BackgroundColor = 0xFFE8EEF8, TextAlign = HAlign.Left,
		};
		var watch = CheckboxCellType.Instance;
		var venue = new DropdownCellType(spec.Venues);
		var fill = new ProgressCellType(100, 0xFF4F81BD);
		for (int g = HeaderRows; g <= LastDataRow; g += GroupSize)
		{
			ws.SetRowStyle(g, groupRow);
			ws.MergeRange(new RangePosition(g, 0, 1, ColumnCount));

			// Cell types are assigned per block, as a range — the group row between blocks keeps none.
			int first = g + 1, count = Math.Min(GroupSize - 1, LastDataRow - g);
			if (count <= 0) continue;
			ws.SetCellType(new RangePosition(first, ColWatch, count, 1), watch);
			ws.SetCellType(new RangePosition(first, ColVenue, count, 1), venue);
			ws.SetCellType(new RangePosition(first, ColFill, count, 1), fill);
		}
	}

	private void SetFormat(string code, params int[] cols)
	{
		foreach (int c in cols)
			Sheet.SetNumberFormat(new RangePosition(FirstDataRow, c, DataRows, 1), code);
	}

	// ---- initial values -----------------------------------------------------------------

	private void Fill(SheetSpec spec)
	{
		var ws = Sheet;
		double now = DateTime.Now.ToOADate();
		int instrument = 0, group = 0;
		for (int r = FirstDataRow; r <= LastDataRow; r++)
		{
			if (IsGroupRow(r))
			{
				int members = Math.Min(GroupSize - 1, LastDataRow - r);
				ws.SetText(r, 0, $"  {spec.Groups[group % spec.Groups.Length]}  ·  block {group + 1:N0}  ·  {members} instruments");
				group++;
				continue;
			}

			instrument++;
			double price = Round(spec.MinPrice + _rng.NextDouble() * (spec.MaxPrice - spec.MinPrice));
			_last[r] = _open[r] = _high[r] = _low[r] = _vwap[r] = price;
			_volume[r] = _rng.Next(1_000, 500_000);
			_position[r] = (_rng.Next(-50, 51)) * 100;

			ws.SetBoolean(r, ColWatch, _rng.Next(12) == 0);
			ws.SetText(r, ColSymbol, $"{spec.Prefix}{instrument:D6}");
			ws.SetText(r, ColName, $"{spec.Groups[(group - 1) % spec.Groups.Length]} #{instrument:N0}");
			ws.SetText(r, ColVenue, spec.Venues[instrument % spec.Venues.Length]);
			ws.SetNumber(r, ColOpen, price);
			Write(r, price, now);
		}
	}

	// ---- the feed -----------------------------------------------------------------------

	/// <summary>Move one instrument and write its 15 changing fields. Returns the direction of
	/// the last price (+1 up, -1 down, 0 unchanged) so the caller can flash the cell.</summary>
	public int Tick(int row, double nowOa)
	{
		double prev = _last[row];
		double last = Round(prev * (1 + (_rng.NextDouble() - 0.5) * 0.004));
		if (last <= 0) last = prev;
		_volume[row] += _rng.Next(100, 5_000);
		Write(row, last, nowOa);
		return Math.Sign(last - prev);
	}

	/// <summary>Move one instrument's last price only — a single cell.</summary>
	public int TickLast(int row)
	{
		double prev = _last[row];
		double last = Round(prev * (1 + (_rng.NextDouble() - 0.5) * 0.004));
		if (last <= 0) last = prev;
		_last[row] = last;
		Sheet.SetNumber(row, ColLast, last);
		return Math.Sign(last - prev);
	}

	private void Write(int r, double last, double nowOa)
	{
		var ws = Sheet;
		double spread = Math.Max(_tickSize, Round(last * 0.0004));
		_last[r] = last;
		if (last > _high[r]) _high[r] = last;
		if (last < _low[r]) _low[r] = last;
		_vwap[r] = Round(_vwap[r] * 0.95 + last * 0.05);

		ws.SetNumber(r, ColBid, Round(last - spread / 2));
		ws.SetNumber(r, ColAsk, Round(last + spread / 2));
		ws.SetNumber(r, ColBidSize, _rng.Next(1, 500) * 100);
		ws.SetNumber(r, ColAskSize, _rng.Next(1, 500) * 100);
		ws.SetNumber(r, ColSpread, spread);
		ws.SetNumber(r, ColLast, last);
		ws.SetNumber(r, ColChange, Round(last - _open[r]));
		ws.SetNumber(r, ColChangePct, (last - _open[r]) / _open[r]);
		ws.SetNumber(r, ColVolume, _volume[r]);
		ws.SetNumber(r, ColHigh, _high[r]);
		ws.SetNumber(r, ColLow, _low[r]);
		ws.SetNumber(r, ColVwap, _vwap[r]);
		ws.SetValue(r, ColTime, CellValue.Date(nowOa));
		ws.SetNumber(r, ColPnl, Math.Round(_position[r] * (last - _open[r])));
		ws.SetNumber(r, ColFill, _rng.Next(0, 101));
	}

	private double Round(double v) => Math.Round(v / _tickSize) * _tickSize;
}
