using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using static SylverInk.Common;
using static SylverInk.Network;

namespace SylverInk
{
	public partial class NetServer
	{
		public bool Active { get; set; } = false;
		public IPAddress? Address { get; set; }
		public string? AddressCode { get; set; }
		public List<TcpClient> Clients { get; } = [];
		private TcpListener DBServer { get; set; } = new(IPAddress.Any, TcpPort);
		public static string[] DNSAddresses { get; } = [
			"http://checkip.dyndns.org",
			"https://ifconfig.me/ip",
			"https://icanhazip.com"
		];
		public byte? Flags;
		public System.Windows.Shapes.Ellipse? Indicator { get; set; }
		public BackgroundWorker ServerTask { get; set; } = new() { WorkerSupportsCancellation = true };
		public bool Serving { get; private set; } = false;
		public BackgroundWorker WatchTask { get; set; } = new() { WorkerSupportsCancellation = true };

		public NetServer(Database DB)
		{
			Indicator = new() { StrokeThickness = 1.0 };

			ServerTask.DoWork += (sender, _) =>
			{
				var task = (BackgroundWorker?)sender;
				while (!task?.CancellationPending is true)
				{
					foreach (var client in Clients)
					{
						try
						{
							if (client.Available > 0)
								Application.Current.Dispatcher.Invoke(() => ReadFromStream(client, DB));
						}
						catch
						{
							Application.Current.Dispatcher.Invoke(() => Close());
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
				var task = (BackgroundWorker?)sender;
				while (!task?.CancellationPending is true)
				{
					if (DBServer.Pending())
					{
						var client = DBServer.AcceptTcpClient();
						SpinWait.SpinUntil(() => client.Available != 0);
						var stream = client.GetStream();
						Flags = (byte)stream.ReadByte();

						var data = DB.Controller?.SerializeRecords(true);
						int dataLength = data?.Count ?? 0;

						data?.Insert(0, (byte)MessageType.DatabaseInit);
						data?.InsertRange(1, [
							0, 0, 0, 0,
							(byte)((dataLength >> 24) & 0xFF),
							(byte)((dataLength >> 16) & 0xFF),
							(byte)((dataLength >> 8) & 0xFF),
							(byte)(dataLength & 0xFF),
						]);

						if (dataLength > 0)
							stream.WriteAsync(data?.ToArray()).AsTask().Wait();

						Clients.Add(client);
					}
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
			DBServer?.Stop();
			DBServer?.Dispose();
			Serving = false;
			Active = false;
			UpdateIndicator();
			UpdateContextMenu();
		}

		private void ReadFromStream(TcpClient client, Database DB)
		{
			int oldData = client.Available;
			bool dataFinished = false;
			do
			{
				dataFinished = !SpinWait.SpinUntil(() => client.Available != oldData, 200);
				oldData = client.Available;
			} while (!dataFinished);

			var stream = client.GetStream();
			var outBuffer = new List<byte>();

			var type = (MessageType)stream.ReadByte();
			outBuffer.Add((byte)type);

			var intBuffer = new byte[4];
			var recordIndex = 0;
			byte[] textBuffer;
			var textCount = 0;

			stream.Read(intBuffer, 0, 4);
			recordIndex = (intBuffer[0] << 24)
				+ (intBuffer[1] << 16)
				+ (intBuffer[2] << 8)
				+ intBuffer[3];

			outBuffer.AddRange(intBuffer);

			switch (type)
			{
				case MessageType.RecordAdd:
					stream.Read(intBuffer, 0, 4);
					textCount = (intBuffer[0] << 24)
						+ (intBuffer[1] << 16)
						+ (intBuffer[2] << 8)
						+ intBuffer[3];
					outBuffer.AddRange(intBuffer);

					if (textCount > 0)
					{
						textBuffer = new byte[textCount];
						stream.Read(textBuffer, 0, textCount);
						outBuffer.AddRange(textBuffer);

						DB?.CreateRecord(Encoding.UTF8.GetString(textBuffer));
						break;
					}

					DB?.CreateRecord(string.Empty);
					break;
				case MessageType.RecordLock:
					DB?.Lock(recordIndex);
					break;
				case MessageType.RecordRemove:
					DB?.DeleteRecord(recordIndex);
					break;
				case MessageType.RecordUnlock:
					DB?.Unlock(recordIndex);
					break;
				case MessageType.TextInsert:
					stream.Read(intBuffer, 0, 4);
					textCount = (intBuffer[0] << 24)
						+ (intBuffer[1] << 16)
						+ (intBuffer[2] << 8)
						+ intBuffer[3];
					outBuffer.AddRange(intBuffer);

					if (textCount > 0)
					{
						textBuffer = new byte[textCount];
						stream.Read(textBuffer, 0, textCount);
						outBuffer.AddRange(textBuffer);

						DB?.CreateRevision(recordIndex, Encoding.UTF8.GetString(textBuffer));
						break;
					}

					DB?.CreateRevision(recordIndex, string.Empty);
					break;
			}

			foreach (var otherClient in Clients)
				if (!otherClient.Equals(client))
					otherClient.GetStream().Write(outBuffer.ToArray());
		}

		public async static void Send(TcpClient client, MessageType type = MessageType.TextInsert, params byte[] data)
		{
			if (!client.Connected)
				return;

			byte[] id = [(byte)type];
			byte[] streamData = [.. id, .. data];

			await client.GetStream().WriteAsync(streamData);
		}

		public async void Serve(byte Flags)
		{
			Active = true;
			Address = IPAddress.Loopback;
			Serving = false;
			UpdateIndicator();

			this.Flags = Math.Min((byte)15, Flags);

			for (int i = 0; i < DNSAddresses.Length; i++)
			{
				try
				{
					var DNSAddress = DNSAddresses[i];
					if (IPAddress.TryParse(await new HttpClient().GetStringAsync(DNSAddress), out var address))
					{
						AddressCode = CodeFromAddress(address, this.Flags);
						Address = CodeToAddress(AddressCode, out this.Flags);
						break;
					}
				}
				catch { }
			}

			if (Address.Equals(IPAddress.Loopback))
			{
				MessageBox.Show("Failed to connect to the DNS server. Please try again.", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
				Active = false;
				Serving = false;
				return;
			}

			DBServer = new(IPAddress.Any, TcpPort);
			DBServer.Server.ReceiveBufferSize = int.MaxValue;
			DBServer.Server.SendBufferSize = int.MaxValue;

			try
			{
				DBServer.Start(512);
			}
			catch
			{
				MessageBox.Show("Failed to open the database server on port 5192.", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
				Active = false;
				Serving = false;
				return;
			}

			Serving = true;

			WatchTask.RunWorkerAsync();
			UpdateIndicator();
			UpdateContextMenu();

			var codePopup = (Popup?)Application.Current.MainWindow.FindName("CodePopup");
			if (codePopup is null)
				return;

			var codeBox = (System.Windows.Controls.TextBox)codePopup.FindName("CodeBox");
			if (codeBox is null)
				return;

			codePopup.IsOpen = true;
			codeBox.Text = AddressCode ?? "Vm000G";
		}

		public void UpdateIndicator()
		{
			Indicator?.Dispatcher.Invoke(() =>
			{
				Indicator.Fill = Serving ? Brushes.MediumPurple : Brushes.Orange;
				Indicator.Height = 12;
				Indicator.Margin = new(2, 4, 3, 4);
				Indicator.Stroke = Common.Settings.MenuForeground;
				Indicator.Width = 12;
				Indicator.InvalidateVisual();
				UpdateContextMenu();
			});
		}
	}
}
