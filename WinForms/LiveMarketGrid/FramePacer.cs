using System.Runtime.InteropServices;

namespace ReoGridSamples.LiveMarketGrid;

/// <summary>
/// Calls <c>tick</c> on the UI thread once for every frame the display shows — WinForms has no
/// frame callback of its own. A background thread waits for each desktop composition
/// (<c>DwmFlush</c>) and posts the tick. The next tick is posted only after the previous one has
/// been painted (<see cref="FramePainted"/>): a slow repaint lowers the rate instead of queueing
/// ticks, and the UI thread keeps time for input, timers and the rest of the window.
/// </summary>
internal sealed class FramePacer : IDisposable
{
	private readonly Control _owner;
	private readonly Action _tick;
	private volatile bool _running;
	private int _waiting;   // 1 from posting a tick until its frame is painted
	private Thread? _thread;

	public FramePacer(Control owner, Action tick)
	{
		_owner = owner;
		_tick = tick;
	}

	public void Start()
	{
		if (_running) return;
		_running = true;
		Volatile.Write(ref _waiting, 0);
		_thread = new Thread(Loop) { IsBackground = true, Name = "FramePacer" };
		_thread.Start();
	}

	public void Stop() => _running = false;

	/// <summary>Call after the grid has painted: the next frame may carry a new tick.</summary>
	public void FramePainted() => Volatile.Write(ref _waiting, 0);

	public void Dispose() => Stop();

	private void Loop()
	{
		while (_running)
		{
			// Without desktop composition (a remote session, for one) DwmFlush fails at once: pace at 60 Hz.
			if (DwmFlush() < 0) Thread.Sleep(16);
			if (!_running || Interlocked.Exchange(ref _waiting, 1) == 1) continue;
			try
			{
				_owner.BeginInvoke(() =>
				{
					if (_running) _tick();
					else Volatile.Write(ref _waiting, 0);
				});
			}
			catch (InvalidOperationException) { _running = false; }   // the window is gone
		}
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmFlush();
}
