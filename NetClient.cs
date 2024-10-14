using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using static SylverInk.Common;
using static SylverInk.Network;

namespace SylverInk
{
	public partial class NetClient
	{
		public static List<char> CodeValues { get; } = Enumerable.Range(48, 10).Concat(Enumerable.Range(65, 26)).Concat(Enumerable.Range(97, 26)).Concat([33, 35, 36, 37]).Select(c => (char)c).ToList();
		public static Dictionary<int, int> ValueCodes { get; } = new(CodeValues.Select((c, i) => new KeyValuePair<int, int>(c, i)));

		public bool Active { get; set; } = false;
		public IPAddress? Address { get; set; }
		public string? AddressCode { get; set; }
		private BackgroundWorker ClientTask { get; set; } = new();
		public bool Connected { get; private set; } = false;
		public bool Connecting { get; private set; } = false;
		public Database? DB { get; set; }
		private TcpClient DBClient { get; set; } = new();
		public byte? Flags;
		public System.Windows.Shapes.Ellipse? Indicator { get; set; }

		public NetClient(Database DB)
		{
			this.DB = DB;

			Indicator = new() { StrokeThickness = 1.0 };

			ClientTask.DoWork += (sender, _) =>
			{
				var task = (BackgroundWorker?)sender;
				while (!task?.CancellationPending is true)
					if (DBClient.Available > 0)
						ReadFromStream();
			};

			ClientTask.RunWorkerCompleted += (_, _) =>
			{
				foreach (var revision in Revisions)
					revision.Key.Add(revision.Value);
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
				MessageBox.Show("You are not connected to the internet.", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}

			Connecting = true;
			UpdateIndicator();

			int oldData = 0;
			bool dataFinished = false;
			do
			{
				dataFinished = (DBClient.Available > 0 && DBClient.Available == oldData);
				oldData = DBClient.Available;
				SpinWait.SpinUntil(() => false, 100);
			} while (!dataFinished);

			byte[] data = new byte[DBClient.Available];
			DBClient.GetStream().Read(data, 0, DBClient.Available);

			DB?.Controller.DeserializeRecords([.. data]);

			ClientTask.RunWorkerAsync();
			Connecting = false;
			Connected = true;

			UpdateIndicator();
		}

		public async void Disconnect()
		{
			ClientTask.CancelAsync();
			DBClient?.Close();
			await Task.Run(() => SpinWait.SpinUntil(() => !DBClient?.Connected is true));
			Connected = false;
			Active = false;
			UpdateIndicator();
		}

		private void ReadFromStream()
		{
			int oldData = DBClient.Available;
			bool dataFinished = false;
			do
			{
				dataFinished = !SpinWait.SpinUntil(() => DBClient.Available != oldData, 200);
				oldData = DBClient.Available;
			} while (!dataFinished);

			var stream = DBClient.GetStream();

			var type = (MessageType)stream.ReadByte();

			var intBuffer = new byte[4];
			var recordIndex = 0;
			byte[] textBuffer;
			var textCount = 0;

			stream.Read(intBuffer, 0, 4);
			recordIndex = (intBuffer[0] << 24)
				+ (intBuffer[1] << 16)
				+ (intBuffer[2] << 8)
				+ intBuffer[3];

			switch (type)
			{
				case MessageType.RecordAdd:
					stream.Read(intBuffer, 0, 4);
					textCount = (intBuffer[0] << 24)
						+ (intBuffer[1] << 16)
						+ (intBuffer[2] << 8)
						+ intBuffer[3];

					if (textCount > 0)
					{
						textBuffer = new byte[textCount];
						stream.Read(textBuffer, 0, textCount);

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

					if (textCount > 0)
					{
						textBuffer = new byte[textCount];
						stream.Read(textBuffer, 0, textCount);

						DB?.CreateRevision(recordIndex, Encoding.UTF8.GetString(textBuffer));
						break;
					}

					DB?.CreateRevision(recordIndex, string.Empty);
					break;
			}
		}

		public async void Send(MessageType type, byte[] data)
		{
			if (!DBClient.Connected)
				return;

			byte[] id = [(byte)type];
			byte[] streamData = [.. id, .. data];

			await DBClient.GetStream().WriteAsync(streamData);
		}

		public void UpdateIndicator()
		{
			Indicator?.Dispatcher.Invoke(() =>
			{
				Indicator.Fill = Connecting ? Brushes.Yellow : (DBClient?.Connected is true ? Brushes.Green : Brushes.Orange);
				Indicator.Height = 15;
				Indicator.Margin = new(4);
				Indicator.Stroke = Common.Settings.MenuForeground;
				Indicator.Width = 15;
				Indicator.InvalidateVisual();
				DB?.GetHeader();
				UpdateContextMenu();
				DeferUpdateRecentNotes(true);
			});
		}
	}
}
