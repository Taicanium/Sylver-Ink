using SylverInk.Notes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Shapes;
using static SylverInk.CommonUtils;
using static SylverInk.XAMLUtils.DataUtils;

namespace SylverInk.Net;

/// <summary>
/// Static helper functions serving common networking routines shared between NetClient and NetServer.
/// </summary>
public static class NetworkUtils
{
	public enum IndicatorStatus
	{
		Connected,
		Connecting,
		Inactive,
		Serving,
	}

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

	public static List<char> CodeValues { get; } = [..
		Enumerable.Range(48, 10) // [0-9]
		.Concat(Enumerable.Range(65, 26)) // [A-Z]
		.Concat(Enumerable.Range(97, 26)) // [a-z]
		.Concat([33, 35, 36, 37]) // ! # $ %
		.Select(c => (char)c)];
	public static string LoopbackCode { get; } = "Vm000G";
	public static int TcpPort { get; } = 5192;
	public static Dictionary<int, int> ValueCodes { get; } = new(CodeValues.Select(static (c, i) => new KeyValuePair<int, int>(c, i)));

	public static string CodeFromAddress(IPAddress? Address, byte? Flags)
	{
		if (Address?.GetAddressBytes() is not byte[] workingList)
			return LoopbackCode;

		return string.Concat<char>([
			CodeValues[(workingList[0] & 252) >> 2],
			CodeValues[((workingList[0] & 3) << 4) + ((workingList[1] & 240) >> 4)],
			CodeValues[((workingList[1] & 15) << 2) + ((workingList[2] & 192) >> 6)],
			CodeValues[workingList[2] & 63],
			CodeValues[(workingList[3] & 252) >> 2],
			CodeValues[((workingList[3] & 3) << 4) + ((Flags ?? 0) & 15)],
		]);
	}

	public static IPAddress CodeToAddress(string? Code, out byte? Flags)
	{
		if (Code?.Select(c => ValueCodes[c]).ToList() is not List<int> workingList)
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

		return new([..convertedList]);
	}

	public static async Task<byte[]> ReadFromStream(TcpClient client, Database? DB)
	{
		int oldData;

		await Task.Run(() =>
		{
			do
			{
				oldData = client.Available;
			} while (SpinWait.SpinUntil(new(() => oldData != client.Available), 500));
		});

		var stream = client.GetStream();
		var outBuffer = new List<byte>();

		var type = (MessageType)stream.ReadByte();
		outBuffer.Add((byte)type);

		var bufferString = string.Empty;
		var intBuffer = new byte[4];
		var recordIndex = 0;
		byte[] textBuffer;
		var textCount = 0;

		stream.ReadExactly(intBuffer, 0, 4);
		recordIndex = IntFromBytes(intBuffer);
		outBuffer.AddRange(intBuffer);

		switch (type)
		{
			case MessageType.RecordAdd:
				stream.ReadExactly(intBuffer, 0, 4);
				textCount = IntFromBytes(intBuffer);
				outBuffer.AddRange(intBuffer);

				if (textCount > 0)
				{
					textBuffer = new byte[textCount];
					stream.ReadExactly(textBuffer, 0, textCount);
					outBuffer.AddRange(textBuffer);
					bufferString = Encoding.UTF8.GetString(textBuffer);
				}

				Concurrent(() => DB?.CreateRecord(bufferString, false));
				DeferUpdateRecentNotes();
				break;
			case MessageType.RecordLock:
				Concurrent(() => DB?.Lock(recordIndex));
				break;
			case MessageType.RecordRemove:
				Concurrent(() => DB?.DeleteRecord(recordIndex, false));
				DeferUpdateRecentNotes();
				break;
			case MessageType.RecordReplace:
				stream.ReadExactly(intBuffer, 0, 4);
				textCount = IntFromBytes(intBuffer);

				if (textCount <= 0)
					break;

				textBuffer = new byte[textCount];
				stream.ReadExactly(textBuffer, 0, textCount);
				bufferString = Encoding.UTF8.GetString(textBuffer);

				stream.ReadExactly(intBuffer, 0, 4);
				textCount = IntFromBytes(intBuffer);

				if (textCount <= 0)
					break;

				textBuffer = new byte[textCount];
				stream.ReadExactly(textBuffer, 0, textCount);

				Concurrent(() => DB?.Replace(bufferString, Encoding.UTF8.GetString(textBuffer), false));
				DeferUpdateRecentNotes();
				break;
			case MessageType.RecordUnlock:
				Concurrent(() => DB?.Unlock(recordIndex));
				break;
			case MessageType.TextInsert:
				stream.ReadExactly(intBuffer, 0, 4);
				textCount = IntFromBytes(intBuffer);
				outBuffer.AddRange(intBuffer);

				if (textCount > 0)
				{
					textBuffer = new byte[textCount];
					stream.ReadExactly(textBuffer, 0, textCount);
					outBuffer.AddRange(textBuffer);
					bufferString = Encoding.UTF8.GetString(textBuffer);
				}

				Concurrent(() => DB?.CreateRevision(recordIndex, bufferString, false));
				DeferUpdateRecentNotes();
				break;
		}

		return [.. outBuffer];
	}

	public static void UpdateIndicator(Ellipse? Indicator, IndicatorStatus Status) => Indicator?.Dispatcher.Invoke(() =>
	{
		if (Indicator.Tag is not Database DB)
			return;

		Indicator.Fill = Status switch
		{
			IndicatorStatus.Connected => Brushes.Green,
			IndicatorStatus.Connecting => Brushes.Yellow,
			IndicatorStatus.Serving => Brushes.MediumPurple,
			IndicatorStatus.Inactive => Brushes.Orange,
			_ => Brushes.Transparent
		};

		Indicator.Height = 12;
		Indicator.Margin = new(2, 4, 3, 4);
		Indicator.Stroke = CommonUtils.Settings.MenuForeground;
		Indicator.Width = 12;
		Indicator.InvalidateVisual();

		Concurrent(DB.RefreshHeader);

		DeferUpdateRecentNotes();
	});
}
