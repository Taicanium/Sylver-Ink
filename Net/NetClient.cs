using SylverInk.Notes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using static SylverInk.Common;
using static SylverInk.Net.Network;

namespace SylverInk.Net;

public partial class NetClient
{
	public bool Active { get; set; }
	public IPAddress? Address { get; set; }
	public string? AddressCode { get; set; }
	private BackgroundWorker ClientTask { get; set; } = new() { WorkerSupportsCancellation = true };
	public bool Connected { get; private set; }
	public bool Connecting { get; private set; }
	public Database? DB { get; set; }
	private TcpClient DBClient { get; set; } = new();
	public byte? Flags;
	public System.Windows.Shapes.Ellipse? Indicator { get; set; }

	public NetClient(Database DB)
	{
		this.DB = DB;

		Indicator = new() { StrokeThickness = 1.0 };

		ClientTask.DoWork += async (sender, _) =>
		{
			var task = (BackgroundWorker?)sender;
			while (!task?.CancellationPending is true)
			{
				try
				{
					if (DBClient.Available > 0)
						await ReadFromStream();

					if (!DBClient.Connected || !DBClient.GetStream().Socket.Connected)
						Concurrent(Disconnect);
				}
				catch
				{
					Concurrent(Disconnect);
				}
			}
		};
	}

	public async Task Connect(string code)
	{
		if (code.Length != 6)
			return;

		Active = true;

		Address = CodeToAddress(code, out Flags);
		AddressCode = CodeFromAddress(Address, Flags);

		if (DBClient.Connected)
			Disconnect();

		Connecting = true;
		UpdateIndicator();

		DBClient = new()
		{
			ReceiveBufferSize = int.MaxValue,
			SendBufferSize = int.MaxValue
		};

		try
		{
			await DBClient.ConnectAsync(Address, TcpPort);
			await DBClient.GetStream().WriteAsync(new List<byte>([Flags ?? 0]).ToArray());
		}
		catch
		{
			MessageBox.Show("Failed to connect to the database.", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
			if (DB is not null)
				Concurrent(() => RemoveDatabase(DB));
			return;
		}

		ClientTask.RunWorkerAsync();
	}

	public async void Disconnect()
	{
		ClientTask.CancelAsync();
		DBClient.Close();
		await Task.Run(() => SpinWait.SpinUntil(new(() => !DBClient.Connected)));
		Active = false;
		Connected = false;
		UpdateIndicator();
	}

	private async Task ReadFromStream()
	{
		int oldData;

		await Task.Run(() =>
		{
			do
			{
				oldData = DBClient.Available;
			} while (SpinWait.SpinUntil(new(() => oldData != DBClient.Available), 500));
		});

		var stream = DBClient.GetStream();
		var type = (MessageType)stream.ReadByte();

		var bufferString = string.Empty;
		var intBuffer = new byte[4];
		var recordIndex = 0;
		byte[] textBuffer;
		var textCount = 0;

		stream.ReadExactly(intBuffer, 0, 4);
		recordIndex = IntFromBytes(intBuffer);

		switch (type)
		{
			case MessageType.DatabaseInit:
				stream.ReadExactly(intBuffer, 0, 4);
				textCount = IntFromBytes(intBuffer);

				if (textCount <= 0)
					break;

				textBuffer = new byte[textCount];
				stream.ReadExactly(textBuffer, 0, textCount);

				DB?.DeserializeRecords([.. textBuffer]);

				Connecting = false;
				Connected = true;
				UpdateIndicator();
				DB?.GetHeader();
				DeferUpdateRecentNotes();

				break;
			case MessageType.RecordAdd:
				stream.ReadExactly(intBuffer, 0, 4);
				textCount = IntFromBytes(intBuffer);

				if (textCount > 0)
				{
					textBuffer = new byte[textCount];
					stream.ReadExactly(textBuffer, 0, textCount);
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

				if (textCount > 0)
				{
					textBuffer = new byte[textCount];
					stream.ReadExactly(textBuffer, 0, textCount);
					bufferString = Encoding.UTF8.GetString(textBuffer);
				}

				Concurrent(() => DB?.CreateRevision(recordIndex, bufferString, false));
				DeferUpdateRecentNotes();
				break;
		}
	}

	public async void Send(MessageType type, byte[] data)
	{
		if (!DBClient.Connected)
			return;

		byte[] id = [(byte)type];
		byte[] streamData = [.. id, .. data];

		try
		{
			await DBClient.GetStream().WriteAsync(streamData);
		}
		catch
		{
			Disconnect();
		}
	}

	public void UpdateIndicator() => Indicator?.Dispatcher.Invoke(() =>
	{
		Indicator.Fill = Connecting ? Brushes.Yellow : DBClient.Connected ? Brushes.Green : Brushes.Orange;
		Indicator.Height = 12;
		Indicator.Margin = new(2, 4, 3, 4);
		Indicator.Stroke = Common.Settings.MenuForeground;
		Indicator.Width = 12;
		Indicator.InvalidateVisual();

		DB?.GetHeader();

		RecentNotesDirty = true;
		DeferUpdateRecentNotes();
	});
}
