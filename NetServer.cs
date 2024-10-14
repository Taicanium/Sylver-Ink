using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
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
		public static string DNSAddress { get; } = "http://checkip.dyndns.org";
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
					foreach (var client in Clients)
						if (client.Available > 0)
							ReadFromStream(client, DB);
			};

			ServerTask.RunWorkerCompleted += (_, _) =>
			{
				foreach (var client in Clients)
					client.Close();

				foreach (var revision in Revisions)
					revision.Key.Add(revision.Value);
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
						var data = DB.Controller?.SerializeRecords(true)?.ToArray();
						stream.WriteAsync(data).AsTask().Wait();
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
			Serving = false;
			Active = false;
			UpdateIndicator();
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
			IPAddress? address;
			Serving = false;
			UpdateIndicator();

			this.Flags = Math.Min((byte)15, Flags);

			try
			{
				if (!IPAddress.TryParse(await new HttpClient().GetStringAsync(DNSAddress), out address))
					address = IPAddress.Loopback;
			}
			catch
			{
				MessageBox.Show("You are not connected to the internet.", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}

			AddressCode = CodeFromAddress(address, this.Flags);
			Address = CodeToAddress(AddressCode, out this.Flags);

			DBServer = new(IPAddress.Any, TcpPort);
			DBServer.Server.ReceiveBufferSize = int.MaxValue;
			DBServer.Server.SendBufferSize = int.MaxValue;
			DBServer.Start(512);
			Serving = true;

			WatchTask.RunWorkerAsync();
			UpdateIndicator();
		}

		public void UpdateIndicator()
		{
			Indicator?.Dispatcher.Invoke(() =>
			{
				Indicator.Fill = Serving ? Brushes.MediumPurple : Brushes.Orange;
				Indicator.Height = 15;
				Indicator.Margin = new(4);
				Indicator.Stroke = Common.Settings.MenuForeground;
				Indicator.Width = 15;
				Indicator.InvalidateVisual();
				DeferUpdateRecentNotes(true);
			});
		}
	}
}
