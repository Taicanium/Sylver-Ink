using SylverInk.Notes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using static SylverInk.CommonUtils;
using static SylverInk.Net.NetworkUtils;

namespace SylverInk.Net;

public partial class NetServer : IDisposable
{
	private IPAddress? Address;
	private readonly List<TcpClient> Clients = [];
	private TcpListener DBServer = new(IPAddress.Any, TcpPort);
	private readonly static string[] DNSAddresses = [
		"http://checkip.dyndns.org",
		"https://ifconfig.me/ip",
		"https://icanhazip.com"
	];
	private byte? Flags;
	private readonly BackgroundWorker ServerTask = new() { WorkerSupportsCancellation = true };
	private readonly BackgroundWorker WatchTask = new() { WorkerSupportsCancellation = true };

	public bool Active { get; private set; }
	public string? AddressCode { get; private set; }
	public System.Windows.Shapes.Ellipse? Indicator { get; private set; }
	public bool Serving { get; private set; }

	public NetServer(Database DB)
	{
		Indicator = new() {
			StrokeThickness = 1.0,
			Tag = DB
		};

		ServerTask.DoWork += async (sender, _) =>
		{
			if (sender is not BackgroundWorker task)
				return;

			while (!task.CancellationPending)
			{
				for (int i = Clients.Count - 1; i > -1; i--)
				{
					try
					{
						var client = Clients[i];

						if (!client.Connected || !client.GetStream().Socket.Connected)
						{
							Clients.RemoveAt(i);
							continue;
						}

						if (client.Available > 0)
							await ReadFromStream(client, DB);
					}
					catch
					{
						Clients[i].Close();
						Clients.RemoveAt(i);
					}
				}
			}
		};

		ServerTask.RunWorkerCompleted += (_, _) =>
		{
			foreach (var client in Clients)
				client.Close();
		};

		WatchTask.DoWork += (sender, _) =>
		{
			if (sender is not BackgroundWorker task)
				return;

			while (!task.CancellationPending)
			{
				if (!DBServer.Pending())
					continue;

				var client = DBServer.AcceptTcpClient();
				SpinWait.SpinUntil(new(() => client.Available != 0));
				var stream = client.GetStream();
				Flags = (byte)stream.ReadByte();

				var data = DB.SerializeRecords(true) ?? [];
				int dataLength = data.Length;

				data = [(byte)MessageType.DatabaseInit, 0, 0, 0, 0, .. IntToBytes(dataLength), .. data];

				if (dataLength > 0)
					stream.WriteAsync(data).AsTask().Wait();

				Clients.Add(client);
			}
		};
	}

	public async void Broadcast(MessageType type, byte[] data)
	{
		foreach (var client in Clients)
		{
			if (!client.Connected)
				continue;

			byte[] id = [(byte)type];
			byte[] streamData = [.. id, .. data];

			await client.GetStream().WriteAsync(streamData);
		}
	}

	public void Close()
	{
		WatchTask.CancelAsync();
		ServerTask.CancelAsync();

		DBServer.Stop();

		Active = false;
		Serving = false;

		UpdateIndicator(Indicator, IndicatorStatus.Inactive);
	}

	protected virtual void Dispose(bool disposing)
	{
		DBServer.Dispose();
		ServerTask.Dispose();
		WatchTask.Dispose();
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	private async Task ReadFromStream(TcpClient client, Database DB)
	{
		var outBuffer = await NetworkUtils.ReadFromStream(client, DB);

		for (int i = Clients.Count - 1; i > -1; i--)
		{
			if (Clients[i].Equals(client))
				continue;

			try
			{
				Clients[i].GetStream().Write(outBuffer);
			}
			catch
			{
				Clients[i].Close();
				Clients.RemoveAt(i);
			}
		}
	}

	public async void Serve(byte Flags)
	{
		Active = true;
		Address = IPAddress.Loopback;
		Serving = false;
		UpdateIndicator(Indicator, IndicatorStatus.Inactive);
		
		this.Flags = Math.Min((byte)15, Flags);

		for (int i = 0; i < DNSAddresses.Length; i++)
		{
			try
			{
				if (IPAddress.TryParse(await new HttpClient().GetStringAsync(DNSAddresses[i]), out var address))
				{
					AddressCode = CodeFromAddress(address, this.Flags);
					Address = CodeToAddress(AddressCode, out this.Flags);
					break;
				}
			}
			catch
			{
				continue;
			}
		}

		if (Address.Equals(IPAddress.Loopback))
		{
			MessageBox.Show("Failed to connect to the DNS server. Please try again.", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
			Active = false;
			Serving = false;
			return;
		}

		try
		{
			DBServer = new(IPAddress.Any, TcpPort);
			DBServer.Server.ReceiveBufferSize = int.MaxValue;
			DBServer.Server.SendBufferSize = int.MaxValue;
			DBServer.Start(256);
		}
		catch
		{
			MessageBox.Show($"Failed to open the database server on port {TcpPort}.", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
			Active = false;
			Serving = false;
			return;
		}

		WatchTask.RunWorkerAsync();
		ServerTask.RunWorkerAsync();

		Serving = true;
		UpdateIndicator(Indicator, IndicatorStatus.Serving);

		if (Application.Current.MainWindow.FindName("CodePopup") is not Popup codePopup)
			return;

		if (codePopup.FindName("CodeBox") is not TextBox codeBox)
			return;

		codePopup.IsOpen = true;
		codeBox.Text = AddressCode ?? LoopbackCode;
	}
}
