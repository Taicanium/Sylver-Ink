﻿using SylverInk.Net;
using SylverInk.Notes;
using SylverInk.XAMLUtils;
using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static SylverInk.Common;

namespace SylverInk;

/// <summary>
/// Interaction logic for SearchResult.xaml
/// </summary>
public partial class SearchResult : Window, IDisposable
{
	private readonly BackgroundWorker? AutosaveThread;
	public bool Dragging { get; private set; }
	public Point DragMouseCoords { get; private set; } = new(0, 0);
	private bool Edited;
	private int OriginalRevisionCount;
	private string OriginalText = string.Empty;
	public Database? ResultDatabase { get; set; }
	public NoteRecord? ResultRecord { get; set; }
	public string ResultText { get; set; } = string.Empty;
	public double SnapTolerance { get; } = 20.0;
	private DateTime TimeSinceAutosave = DateTime.Now;

	public SearchResult()
	{
		InitializeComponent();
		DataContext = Common.Settings;

		AutosaveThread = new()
		{
			WorkerSupportsCancellation = true
		};

		AutosaveThread.DoWork += (sender, _) =>
		{
			SpinWait.SpinUntil(new(() => DateTime.Now.Subtract(TimeSinceAutosave).Seconds >= 5.0));
			if (((BackgroundWorker?)sender)?.CancellationPending is true)
				return;
			Concurrent(this.SaveRecord);
			Concurrent(this.Autosave);
		};
	}

	private void CloseClick(object? sender, RoutedEventArgs e)
	{
		if (AutosaveThread?.IsBusy is true)
			AutosaveThread?.CancelAsync();

		if (Edited)
		{
			switch(MessageBox.Show("You have unsaved changes. Save before closing this note?", "Sylver Ink: Notification", MessageBoxButton.YesNoCancel, MessageBoxImage.Information))
			{
				case MessageBoxResult.Cancel:
					return;
				case MessageBoxResult.No:
					ResultBlock.Document = XamlToFlowDocument(OriginalText);
					ResultText = OriginalText;
					Edited = false;
					for (int i = (ResultRecord?.GetNumRevisions() ?? 1) - 1; i >= OriginalRevisionCount; i--)
						ResultRecord?.DeleteRevision(i);
					RecentNotesDirty = true;
					DeferUpdateRecentNotes();
					break;
			}
		}

		PreviousOpenNote = ResultRecord;
		Close();
	}

	public void Dispose()
	{
		AutosaveThread?.Dispose();
		GC.SuppressFinalize(this);
	}

	private void Result_Closed(object? sender, EventArgs e)
	{
		if (AutosaveThread?.IsBusy is true)
			AutosaveThread?.CancelAsync();

		if (Edited)
			this.SaveRecord();

		ResultDatabase?.Transmit(Network.MessageType.RecordUnlock, IntToBytes(ResultRecord?.Index ?? 0));

		foreach (SearchResult result in OpenQueries)
		{
			if (result.ResultRecord != ResultRecord)
				continue;

			OpenQueries.Remove(result);
			return;
		}
	}

	private void Result_Loaded(object? sender, RoutedEventArgs e)
	{
		if (ResultRecord?.Locked is true)
		{
			LastChangedLabel.Content = "Locked by another user";
			ResultBlock.IsEnabled = false;
		}
		else
		{
			LastChangedLabel.Content = ResultRecord?.GetLastChange();
			ResultDatabase?.Transmit(Network.MessageType.RecordUnlock, IntToBytes(ResultRecord?.Index ?? 0));
		}

		try
		{
			ResultBlock.Document = XamlToFlowDocument(ResultText);
		}
		catch
		{
			ResultBlock.Document = PlaintextToFlowDocument(ResultText);
		}

		Edited = false;
		OriginalRevisionCount = ResultRecord?.GetNumRevisions() ?? 0;
		OriginalText = ResultText;

		var tabPanel = GetChildPanel("DatabasesPanel");
		for (int i = tabPanel.Items.Count - 1; i > 0; i--)
		{
			var item = (TabItem)tabPanel.Items[i];
			if (item.Tag is not NoteRecord record)
				continue;

			if (record.Equals(ResultRecord) is true)
				tabPanel.Items.RemoveAt(i);
		}
	}

	private void ResultBlock_TextChanged(object? sender, TextChangedEventArgs e)
	{
		Edited = true;
		RecentNotesDirty = true;
		if (AutosaveThread?.IsBusy is true)
			return;
		TimeSinceAutosave = DateTime.Now;
		AutosaveThread?.RunWorkerAsync();
	}

	private void ViewClick(object? sender, RoutedEventArgs e)
	{
		SearchWindow?.Close();
		this.AddTabToRibbon();
	}

	private void WindowActivated(object? sender, EventArgs e)
	{
		CloseButton.IsEnabled = true;
		Opacity = 1.0;
		ViewButton.IsEnabled = true;
	}

	private void WindowDeactivated(object? sender, EventArgs e)
	{
		CloseButton.IsEnabled = false;
		Opacity = 1.0 - (Common.Settings.NoteTransparency / 100.0);
		ViewButton.IsEnabled = false;
	}

	private void WindowMove(object? sender, MouseEventArgs e) => this.Drag(sender, e);

	private void WindowMouseDown(object? sender, MouseButtonEventArgs e)
	{
		var n = PointToScreen(e.GetPosition(null));
		CaptureMouse();
		DragMouseCoords = new(Left - n.X, Top - n.Y);
		Dragging = true;
	}

	private void WindowMouseUp(object? sender, MouseButtonEventArgs e)
	{
		ReleaseMouseCapture();
		DragMouseCoords = new(0, 0);
		Dragging = false;
	}
}
