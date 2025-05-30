using SylverInk.Notes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using static SylverInk.Common;
using static SylverInk.Net.Network;

namespace SylverInk.Net;

public partial class NetClient
{
	public bool Active { get; private set; }
	private IPAddress? Address { get; set; }
	private BackgroundWorker ClientTask { get; set; } = new() { WorkerSupportsCancellation = true };
	public bool Connected { get; private set; }
	private Database? DB { get; set; }
	private TcpClient DBClient { get; set; } = new();
	private byte? Flags;
	public System.Windows.Shapes.Ellipse? Indicator { get; private set; }

	public NetClient(Database DB)
	{
		this.DB = DB;

		Indicator = new() { StrokeThickness = 1.0 };
		Indicator.LayoutUpdated += (_, _) => this.DB.GetHeader();

		ClientTask.DoWork += async (sender, _) =>
		{
			var task = (BackgroundWorker?)sender;
			while (!task?.CancellationPending is true)
			{
				try
				{
					if (DBClient.Available > 0)
						await ReadFromStream(DBClient, this.DB);

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

		if (DBClient.Connected)
			Disconnect();

		UpdateIndicator(Indicator, IndicatorStatus.Connecting);

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
		UpdateIndicator(Indicator, IndicatorStatus.Inactive);
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
}
