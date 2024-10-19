﻿using System;
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
using static SylverInk.Network;

namespace SylverInk
{
	public partial class NetClient
	{
		public bool Active { get; set; } = false;
		public IPAddress? Address { get; set; }
		public string? AddressCode { get; set; }
		private BackgroundWorker ClientTask { get; set; } = new() { WorkerSupportsCancellation = true };
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
				{
					try
					{
						if (DBClient.Available > 0)
							Application.Current.Dispatcher.Invoke(() => ReadFromStream());
					}
					catch
					{
						Application.Current.Dispatcher.Invoke(() => Disconnect());
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
				return;
			}

			ClientTask.RunWorkerAsync();
		}

		public async void Disconnect()
		{
			ClientTask.CancelAsync();
			DBClient?.Close();
			await Task.Run(() => SpinWait.SpinUntil(() => !DBClient?.Connected is true));
			DBClient?.Dispose();
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
				case MessageType.DatabaseInit:
					stream.Read(intBuffer, 0, 4);
					textCount = (intBuffer[0] << 24)
						+ (intBuffer[1] << 16)
						+ (intBuffer[2] << 8)
						+ intBuffer[3];

					if (textCount > 0)
					{
						textBuffer = new byte[textCount];
						stream.Read(textBuffer, 0, textCount);

						DB?.Controller.DeserializeRecords(new(textBuffer));

						Connecting = false;
						Connected = true;
						UpdateIndicator();
						DB?.GetHeader();
						Application.Current.Dispatcher.Invoke(() => DeferUpdateRecentNotes(true));
					}
					break;
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

						DB?.CreateRevision(recordIndex, Encoding.UTF8.GetString(textBuffer), true);
						break;
					}

					DB?.CreateRevision(recordIndex, string.Empty, true);
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
				Indicator.Height = 12;
				Indicator.Margin = new(2, 4, 3, 4);
				Indicator.Stroke = Common.Settings.MenuForeground;
				Indicator.Width = 12;
				Indicator.InvalidateVisual();
				DB?.GetHeader();
				UpdateContextMenu();
			});
		}
	}
}
