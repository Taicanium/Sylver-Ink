using SylverInk.Notes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using static SylverInk.Common;
using static SylverInk.Net.Network;

namespace SylverInk.Net
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
					Concurrent(() =>
					{
						for (int i = Clients.Count - 1; i > -1; i--)
						{
							try
							{
								var client = Clients[i];

								if (client.Available > 0)
									ReadFromStream(client, DB);

								if (!client.Connected || !client.GetStream().Socket.Connected)
									Clients.RemoveAt(i);
							}
							catch
							{
								//TODO: A fault in one connection really shouldn't be grounds for panicking the entire server. This is safe, but it's overkill.
								Close();
							}
						}
					});
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
					if (!DBServer.Pending())
						continue;

					var client = DBServer.AcceptTcpClient();
					SpinWait.SpinUntil(new(() => client.Available != 0));
					var stream = client.GetStream();
					Flags = (byte)stream.ReadByte();

					var data = DB.SerializeRecords(true);
					int dataLength = data?.Count ?? 0;

					data?.Insert(0, (byte)MessageType.DatabaseInit);
					data?.InsertRange(1, [
						0, 0, 0, 0,
							.. IntToBytes(dataLength)
					]);

					if (dataLength > 0)
						stream.WriteAsync(data?.ToArray()).AsTask().Wait();

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
				dataFinished = !SpinWait.SpinUntil(new(() => client.Available != oldData), 200);
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
			recordIndex = IntFromBytes(intBuffer);
			outBuffer.AddRange(intBuffer);

			switch (type)
			{
				case MessageType.RecordAdd:
					stream.Read(intBuffer, 0, 4);
					textCount = IntFromBytes(intBuffer);
					outBuffer.AddRange(intBuffer);

					if (textCount > 0)
					{
						textBuffer = new byte[textCount];
						stream.Read(textBuffer, 0, textCount);
						outBuffer.AddRange(textBuffer);

						DB?.CreateRecord(Encoding.UTF8.GetString(textBuffer), false);
						Concurrent(() => DeferUpdateRecentNotes());
						break;
					}

					DB?.CreateRecord(string.Empty, false);
					Concurrent(() => DeferUpdateRecentNotes());
					break;
				case MessageType.RecordLock:
					DB?.Lock(recordIndex);
					break;
				case MessageType.RecordRemove:
					DB?.DeleteRecord(recordIndex, false);
					Concurrent(() => DeferUpdateRecentNotes());
					break;
				case MessageType.RecordReplace:
					stream.Read(intBuffer, 0, 4);
					textCount = IntFromBytes(intBuffer);

					if (textCount > 0)
					{
						textBuffer = new byte[textCount];
						stream.Read(textBuffer, 0, textCount);
						var oldText = Encoding.UTF8.GetString(textBuffer);

						stream.Read(intBuffer, 0, 4);
						textCount = IntFromBytes(intBuffer);

						if (textCount > 0)
						{
							textBuffer = new byte[textCount];
							stream.Read(textBuffer, 0, textCount);
							var newText = Encoding.UTF8.GetString(textBuffer);

							DB?.Replace(oldText, newText, false);
							Concurrent(() => DeferUpdateRecentNotes());
						}
					}
					break;
				case MessageType.RecordUnlock:
					DB?.Unlock(recordIndex);
					break;
				case MessageType.TextInsert:
					stream.Read(intBuffer, 0, 4);
					textCount = IntFromBytes(intBuffer);
					outBuffer.AddRange(intBuffer);

					if (textCount > 0)
					{
						textBuffer = new byte[textCount];
						stream.Read(textBuffer, 0, textCount);
						outBuffer.AddRange(textBuffer);

						DB?.CreateRevision(recordIndex, Encoding.UTF8.GetString(textBuffer), false);
						Concurrent(() => DeferUpdateRecentNotes());
						break;
					}

					DB?.CreateRevision(recordIndex, string.Empty, false);
					Concurrent(() => DeferUpdateRecentNotes());
					break;
			}

			foreach (var otherClient in Clients)
			{
				if (otherClient.Equals(client))
					continue;

				try
				{
					otherClient.GetStream().Write(outBuffer.ToArray());
				}
				catch
				{
					otherClient.Close();
				}
			}
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
				DBServer.Start(256);
			}
			catch
			{
				MessageBox.Show($"Failed to open the database server on port {TcpPort}.", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
				Active = false;
				Serving = false;
				return;
			}

			Serving = true;

			WatchTask.RunWorkerAsync();
			ServerTask.RunWorkerAsync();
			UpdateIndicator();
			UpdateContextMenu();

			var codePopup = (Popup?)Application.Current.MainWindow.FindName("CodePopup");
			if (codePopup is null)
				return;

			var codeBox = (TextBox)codePopup.FindName("CodeBox");
			if (codeBox is null)
				return;

			codePopup.IsOpen = true;
			codeBox.Text = AddressCode ?? "Vm000G";
		}

		public void UpdateIndicator() => Indicator?.Dispatcher.Invoke(() =>
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
