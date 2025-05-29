using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using static SylverInk.Common;

namespace SylverInk.Net;

static class UpdateHandler
{
	private static string GitReleasesURI { get; } = "https://api.github.com/repos/taicanium/Sylver-Ink/releases?per_page=1&page=1";
	public static string TempUri { get; } = Path.Join(DocumentsFolder, "SylverInk_Setup.msi");
	public static string UpdateLockUri { get; } = Path.Join(DocumentsFolder, "~si_update.lock");

	public static async Task CheckForUpdates()
	{
		var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
		if (assemblyVersion is null)
			return;

		if (Process.GetCurrentProcess().MainModule?.FileName is null)
			return;

		try
		{
			using var httpClient = new HttpClient();
			if (!httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("request"))
				return;

			var release = JsonSerializer.Deserialize<JsonArray>(await httpClient.GetStringAsync(GitReleasesURI))?[0]?.AsObject();
			if (release is null)
				return;

			if (!release.TryGetPropertyValue("tag_name", out var releaseNode) || !release.TryGetPropertyValue("assets", out var assetNode))
				return;

			var releaseString = releaseNode?.ToString() ?? "0.0.0";
			if (releaseString.StartsWith('v'))
				releaseString = releaseString[1..];

			if (!Version.TryParse(releaseString, out var releaseVersion) || releaseVersion.CompareTo(assemblyVersion) < 1)
				return;

			var assetArray = assetNode?.AsArray();
			if (assetArray is null)
				return;

			string? uriNode = null;

			foreach (var asset in assetArray)
			{
				if (asset is null)
					continue;

				if (!asset.AsObject().TryGetPropertyValue("browser_download_url", out var nValue))
					continue;

				if (nValue is null)
					continue;

				if (nValue.ToString().EndsWith(".msi"))
					uriNode = nValue.ToString();
			}

			if (uriNode is null)
				return;

			if (MessageBox.Show($"A new update is available ({assemblyVersion.ToString(3)} → {releaseVersion.ToString(3)}). Would you like to install it now?", "Sylver Ink: Notification", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.No)
				return;

			await DownloadAndInstallUpdate(httpClient, uriNode);
		}
		catch
		{
			return;
		}
	}

	private static async Task DownloadAndInstallUpdate(HttpClient httpClient, string uriNode)
	{
		Erase(TempUri);
		Erase(UpdateLockUri);

		File.Create(UpdateLockUri, 0).Close();

		await httpClient.DownloadFileTaskAsync(uriNode, TempUri);

		ProcessStartInfo inf = new()
		{
			FileName = TempUri,
			UseShellExecute = true,
		};

		Process.Start(inf);
		Application.Current.Shutdown();
	}
}