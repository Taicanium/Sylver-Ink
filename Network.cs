using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace SylverInk
{
	/// <summary>
	/// Static helper functions serving common networking routines shared between NetClient and NetServer.
	/// </summary>
	public static class Network
	{
		public enum MessageType
		{
			DatabaseInit,
			RecordAdd,
			RecordLock,
			RecordRemove,
			RecordReplace,
			RecordUnlock,
			TextInsert
		}

		public static List<char> CodeValues { get; } = Enumerable.Range(48, 10).Concat(Enumerable.Range(65, 26)).Concat(Enumerable.Range(97, 26)).Concat([33, 35, 36, 37]).Select(c => (char)c).ToList();
		public static Dictionary<int, int> ValueCodes { get; } = new(CodeValues.Select((c, i) => new KeyValuePair<int, int>(c, i)));
		public static int TcpPort { get; } = 5192;

		public static string CodeFromAddress(IPAddress? Address, byte? Flags)
		{
			var workingList = Address?.GetAddressBytes() ?? [127, 0, 0, 1];
			if (workingList.Length != 4)
				return "127.0.0.1";

			var convertedList = new List<char>([
				CodeValues[(workingList[0] & 252) >> 2],
				CodeValues[((workingList[0] & 3) << 4) + ((workingList[1] & 240) >> 4)],
				CodeValues[((workingList[1] & 15) << 2) + ((workingList[2] & 192) >> 6)],
				CodeValues[workingList[2] & 63],
				CodeValues[(workingList[3] & 252) >> 2],
				CodeValues[((workingList[3] & 3) << 4) + ((Flags ?? 0) & 15)],
			]);

			return string.Concat(convertedList);
		}

		public static IPAddress CodeToAddress(string? Code, out byte? Flags)
		{
			var workingList = Code?.Select(c => ValueCodes[c]).ToList() ?? [0, 0, 0, 0, 0, 0];
			if (workingList.Count != 6)
			{
				Flags = 0;
				return IPAddress.Loopback;
			}

			var convertedList = new List<int>([
				(workingList[0] << 2) + ((workingList[1] & 48) >> 4),
				((workingList[1] & 15) << 4) + ((workingList[2] & 60) >> 2),
				((workingList[2] & 3) << 6) + workingList[3],
				(workingList[4] << 2) + ((workingList[5] & 48) >> 4)
			]).Select(c => (byte)c);

			Flags = (byte?)(workingList[5] & 15);

			return new(convertedList.ToArray());
		}
	}
}
