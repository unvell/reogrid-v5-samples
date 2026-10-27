using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using unvell.ReoGrid.WinForms;

namespace ReoGridSamples.LiveMarketGrid;

/// <summary>The machine and build a measurement ran on, for the benchmark report and the demo's results card.</summary>
internal static class SystemInfo
{
	public static string Cpu() =>
		Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) is string s
			? s.Trim() : "unknown CPU";

	/// <summary>The display adapter that drives <paramref name="screen"/>.</summary>
	public static string Gpu(Screen screen)
	{
		var device = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
		for (int i = 0; EnumDisplayDevices(null, i, ref device, 0); i++)
		{
			if (device.DeviceName == screen.DeviceName) return device.DeviceString.Trim();
			device.cb = Marshal.SizeOf<DisplayDevice>();
		}
		return "unknown GPU";
	}

	/// <summary>Refresh rate of <paramref name="screen"/> in Hz, or 0 if Windows does not say.</summary>
	public static int RefreshRate(Screen screen)
	{
		var mode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
		return EnumDisplaySettings(screen.DeviceName, EnumCurrentSettings, ref mode) && mode.dmDisplayFrequency > 1
			? mode.dmDisplayFrequency : 0;
	}

	public static string ReoGridVersion()
	{
		var asm = typeof(ReoGridControl).Assembly;
		return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
			?? asm.GetName().Version?.ToString() ?? "?";
	}

	public static string Windows() => Environment.OSVersion.Version.Build >= 22000 ? "Windows 11" : "Windows 10";

	public static string Configuration() =>
		typeof(SystemInfo).Assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled == true ? "Debug build" : "Release build";

	private const int EnumCurrentSettings = -1;

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DisplayDevice
	{
		public int cb;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
		public int StateFlags;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DevMode
	{
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
		public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
		public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
		public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
		public short dmLogPixels;
		public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
		public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool EnumDisplayDevices(string? device, int index, ref DisplayDevice displayDevice, int flags);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool EnumDisplaySettings(string device, int mode, ref DevMode devMode);
}
