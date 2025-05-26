using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace SylverInk.Net;

public static class HttpClientUtils
{
	/// <summary>
	/// This method encapsulates the process of asynchronously downloading a file from the internet and saving it to a local path.
	/// </summary>
	/// <param name="client">An existing HttpClient object.</param>
	/// <param name="uri">The URI of the file to download.</param>
	/// <param name="FileName">The local file path to save the downloaded file.</param>
	/// <returns>An awaitable <c>Task</c> representing the download operation.</returns>
	public static async Task DownloadFileTaskAsync(this HttpClient client, string uri, string FileName)
	{
		using var stream = await client.GetStreamAsync(uri);
		using var fs = new FileStream(FileName, FileMode.Create);
		await stream.CopyToAsync(fs);
	}
}
