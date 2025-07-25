﻿using SylverInk.Notes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using static SylverInk.CommonUtils;
using static SylverInk.Net.NetworkUtils;
using static SylverInk.Notes.DatabaseUtils;

namespace SylverInk.Net;

public partial class NetClient : IDisposable
{
	private IPAddress? Address;
	private readonly BackgroundWorker ClientTask = new() { WorkerSupportsCancellation = true };
	private readonly Database? DB;
	private TcpClient DBClient = new();
	private byte? Flags;

	public bool Active { get; private set; }
	public bool Connected { get; private set; }
	public System.Windows.Shapes.Ellipse? Indicator { get; private set; }

	public NetClient(Database DB)
	{
		this.DB = DB;

		Indicator = new()
		{
			StrokeThickness = 1.0,
			Tag = this.DB
		};

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

	protected virtual void Dispose(bool disposing)
	{
		ClientTask.Dispose();
		DBClient.Dispose();
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
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
