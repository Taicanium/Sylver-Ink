using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using static SylverInk.FileIO.FileUtils;

namespace SylverInk.Net;

static class UpdateHandler
{
	private static string GitReleasesURI { get; } = "https://api.github.com/repos/taicanium/Sylver-Ink/releases?per_page=1&page=1";
	public static string TempUri { get; } = Path.Join(DocumentsFolder, "SylverInk_Setup.msi");
	public static string UpdateLockUri { get; } = Path.Join(DocumentsFolder, "~si_update.lock");

	public static async Task CheckForUpdates()
	{
		if (Assembly.GetExecutingAssembly().GetName().Version is not Version assemblyVersion)
			return;

		if (Process.GetCurrentProcess().MainModule?.FileName is null)
			return;

		try
		{
			using var httpClient = new HttpClient();
			if (!httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("request"))
				return;

			if (JsonSerializer.Deserialize<JsonArray>(await httpClient.GetStringAsync(GitReleasesURI))?[0]?.AsObject() is not JsonObject release)
				return;

			if (!release.TryGetPropertyValue("tag_name", out var tagNode) || !release.TryGetPropertyValue("assets", out var assetNode))
				return;

			var releaseString = tagNode?.ToString() ?? "0.0.0";
			if (releaseString.StartsWith('v'))
				releaseString = releaseString[1..];

			if (!Version.TryParse(releaseString, out var releaseVersion) || releaseVersion.CompareTo(assemblyVersion) < 1)
				return;

			if (assetNode?.AsArray() is not JsonArray assetArray)
				return;

			string? uriNode = null;

			foreach (var asset in assetArray)
			{
				if (asset is null)
					continue;

				if (!asset.AsObject().TryGetPropertyValue("browser_download_url", out var nValue))
					continue;

				if (nValue?.ToString() is not string nString)
					continue;

				if (nString.EndsWith(".msi"))
					uriNode = nString;
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

		CommonUtils.AbortRun = true;

		Process.Start(new ProcessStartInfo()
		{
			FileName = TempUri,
			UseShellExecute = true,
		});

		Application.Current.Shutdown();
	}
}