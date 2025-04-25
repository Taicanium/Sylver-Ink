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
		private static string TempUri { get; } = Path.Join(DocumentsFolder, "SylverInk_Setup.msi");
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

				var releaseString = release["tag_name"]?.ToString() ?? string.Empty;
				if (releaseString.StartsWith('v'))
					releaseString = releaseString[1..];
				while (releaseString.AsSpan().Count('.') < 3)
					releaseString += ".0";

				var asset = release["assets"]?.AsArray()[0]?.AsObject();
				if (asset is null)
					return;
				if (!asset.TryGetPropertyValue("browser_download_url", out var uriNode))
					return;

				var fileUri = uriNode?.ToString();
				if (fileUri is null)
					return;

				var releaseVersion = Version.Parse(releaseString);
				if (releaseVersion.CompareTo(assemblyVersion) <= 0)
					return;

				if (MessageBox.Show($"A new update is available ({assemblyVersion} → {releaseString}). Would you like to install it now?", "Sylver Ink: Info", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
					return;

				if (File.Exists(TempUri))
					File.Delete(TempUri);

				if (File.Exists(UpdateLockUri))
					File.Delete(UpdateLockUri);

				File.Create(UpdateLockUri, 0).Close();

				await httpClient.DownloadFileTaskAsync(fileUri, TempUri);

				ProcessStartInfo inf = new()
				{
					FileName = TempUri,
					UseShellExecute = false,
				};

				Process.Start(inf);
				Application.Current.Shutdown();
			}
			catch (Exception)
			{
				return;
			}
		}
	}
}