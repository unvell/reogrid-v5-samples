using unvell.ReoGrid.Core.License;

namespace ReoGridSamples.LiveMarketGrid;

/// <summary>
/// Applies a ReoGrid license key if one is available, from the same places the ReoGrid build
/// looks: the <c>REOGRID_LICENSE</c> environment variable, then
/// <c>%LOCALAPPDATA%\unvell\reogrid\license.txt</c>. Without a key the sample still runs —
/// read-only, with the evaluation watermark — which is all a ticking display needs.
/// </summary>
internal static class LicenseLoader
{
	public static string Apply()
	{
		string? key = Environment.GetEnvironmentVariable("REOGRID_LICENSE");
		if (string.IsNullOrWhiteSpace(key))
		{
			string path = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "unvell", "reogrid", "license.txt");
			if (File.Exists(path)) key = File.ReadAllText(path).Trim();
		}

		if (string.IsNullOrWhiteSpace(key)) return "unlicensed (evaluation watermark)";
		try
		{
			ReoGridLicense.SetLicense(key);
			return ReoGridLicense.IsLicensed ? $"licensed ({ReoGridLicense.Current?.Tier})" : "license key not valid";
		}
		catch (ArgumentException)
		{
			return "license key malformed";
		}
	}
}
