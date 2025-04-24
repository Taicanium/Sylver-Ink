using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace SylverInk.Net
{
	public static class HttpClientUtils
	{
		public static async Task DownloadFileTaskAsync(this HttpClient client, string uri, string FileName)
		{
			using var stream = await client.GetStreamAsync(uri);
			using var fs = new FileStream(FileName, FileMode.CreateNew);
			await stream.CopyToAsync(fs);
		}
	}
}
