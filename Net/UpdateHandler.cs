using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using static SylverInk.Common;

namespace SylverInk.Net
{
	static class UpdateHandler
	{
		private static string GitReleasesURI { get; } = "https://api.github.com/repos/taicanium/Sylver-Ink/releases?per_page=1&page=1";
		public static string TempUri { get; } = Path.Join(DocumentsFolder, "SylverInk_Setup.msi");
		public static string UpdateLockUri { get; } = Path.Join(DocumentsFolder, "~si_update.lock");

		public static async void CheckForUpdates()
		{
			var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
			if (assemblyVersion is null)
				return;

			var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
			if (currentExe is null)
				return;

			try
			{
				using var httpClient = new HttpClient();
				if (!httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("request"))
					return;

				string response = await httpClient.GetStringAsync(GitReleasesURI);
				var release = JsonSerializer.Deserialize<JsonArray>(response)?[0]?.AsObject();
				if (release is null)
					return;
				if (!release.ContainsKey("tag_name") || !release.ContainsKey("assets"))
					return;

				var assetArray = release["assets"]?.AsArray();
				if (assetArray is null)
					return;

				var releaseString = release["tag_name"]?.ToString() ?? string.Empty;
				if (releaseString.StartsWith('v'))
					releaseString = releaseString[1..];
				while (releaseString.AsSpan().Count('.') < 2)
					releaseString += ".0";

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

				var releaseVersion = Version.Parse(releaseString);
				if (releaseVersion.CompareTo(assemblyVersion) <= 0)
					return;

				if (MessageBox.Show($"A new update is available ({assemblyVersion.ToString(3)} → {releaseString}). Would you like to install it now?", "Sylver Ink: Notification", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
					return;

				if (File.Exists(TempUri))
					File.Delete(TempUri);

				if (File.Exists(UpdateLockUri))
					File.Delete(UpdateLockUri);

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
			catch
			{
				return;
			}
		}
	}
}