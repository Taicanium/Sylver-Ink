using SylverInk.Net;
using SylverInk.Notes;
using SylverInk.XAML;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static SylverInk.CommonUtils;
using static SylverInk.FileIO.FileUtils;
using static SylverInk.Notes.DatabaseUtils;
using static SylverInk.XAMLUtils.DataUtils;
using static SylverInk.XAMLUtils.TextUtils;

namespace SylverInk.XAMLUtils;

public static class SearchResultUtils
{
	[DllImport("user32.dll")]
	static extern int GetWindowLong(IntPtr hwnd, int index);

	[DllImport("user32.dll")]
	static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

	private const int GWL_EXSTYLE = -20;
	private const int WS_EX_LAYERED = 0x00080000;
	private const int WS_EX_TRANSPARENT = 0x00000020;

	public struct SimplePoint(int x, int y)
	{
		public int X { get; set; } = x;
		public int Y { get; set; } = y;
	}

	public static void AddTabToRibbon(this SearchResult window)
	{
		if (window.ResultRecord is null)
			return;

		if (window.ResultDatabase is null)
			return;

		SwitchDatabase(window.ResultDatabase);

		TabItem item = new()
		{
			Content = new NoteTab() {
				InitialPointer = window.ResultBlock.CaretPosition,
				Record = window.ResultRecord
			},
			Header = GetRibbonHeader(window.ResultRecord),
		};

		var ChildPanel = GetChildPanel("DatabasesPanel");
		ChildPanel.SelectedIndex = ChildPanel.Items.Add(item);
		OpenTabs.Add(item);

		window.StopMonitors();
		window.Close();
	}

	public static void Autosave(this SearchResult window)
	{
		var lockFile = GetLockFile(window.ResultDatabase?.DBFile);
		Erase(lockFile);

		window.ResultRecord?.CreateRevision(FlowDocumentToXaml(window.ResultBlock.Document));
		window.ResultDatabase?.Save(lockFile);
		window.ResultRecord?.DeleteRevision(window.ResultRecord.GetNumRevisions());
	}

	public static void Construct(this SearchResult window)
	{
		if (window.FinishedLoading)
			return;

		if (window.ResultRecord?.Locked is true)
		{
			window.LastChangedLabel.Content = "Locked by another user";
			window.ResultBlock.IsEnabled = false;
		}
		else
		{
			window.LastChangedLabel.Content = window.ResultRecord?.GetLastChange();
			window.ResultDatabase?.Transmit(NetworkUtils.MessageType.RecordUnlock, IntToBytes(window.ResultRecord?.Index ?? 0));
		}

		window.Edited = false;
		window.ResultBlock.Document = window.ResultRecord?.GetDocument() ?? new();

		window.OriginalBlockCount = window.ResultBlock.Document.Blocks.Count;
		window.OriginalRevisionCount = window.ResultRecord?.GetNumRevisions() ?? 0;
		window.OriginalText = FlowDocumentToXaml(window.ResultBlock.Document);

		var tabPanel = GetChildPanel("DatabasesPanel");
		for (int i = tabPanel.Items.Count - 1; i > 0; i--)
		{
			if (tabPanel.Items[i] is not TabItem item)
				continue;

			if (item.Tag is not NoteRecord record)
				continue;

			if (record.Equals(window.ResultRecord) is true)
				tabPanel.Items.RemoveAt(i);
		}

		window.FinishedLoading = true;
	}

	public static void Drag(this SearchResult window, object? sender, MouseEventArgs e)
	{
		if (!window.Dragging)
			return;

		var mouse = window.PointToScreen(e.GetPosition(null));
		var newCoords = new Point()
		{
			X = window.DragMouseCoords.X + mouse.X,
			Y = window.DragMouseCoords.Y + mouse.Y
		};

		if (CommonUtils.Settings.SnapSearchResults)
			window.Snap(ref newCoords);

		window.Left = newCoords.X;
		window.Top = newCoords.Y;
	}

	private static void InitEnterMonitor(SearchResult window)
	{
		window.EnterMonitor = new()
		{
			Interval = new TimeSpan(0, 0, 0, 0, 20)
		};

		window.EnterMonitor.Tick += (_, _) =>
		{
			var Seconds = (DateTime.UtcNow.Ticks - window.EnterTime) * 1E-7;

			if (Seconds > CommonUtils.Settings.NoteClickthrough)
			{
				Concurrent(window.UnsetWindowExTransparent);
				window.Opacity = 1.0;
				window.EnterMonitor.Stop();
				return;
			}

			var tick = Seconds * CommonUtils.Settings.NoteClickthroughInverse;
			window.Opacity = Lerp(window.StartOpacity, 1.0, tick * tick);
		};
	}

	private static void InitLeaveMonitor(SearchResult window)
	{
		window.LeaveMonitor = new()
		{
			Interval = new TimeSpan(0, 0, 0, 0, 20)
		};

		window.LeaveMonitor.Tick += (_, _) =>
		{
			var Seconds = (DateTime.UtcNow.Ticks - window.LeaveTime) * 1E-7;

			if (Seconds > CommonUtils.Settings.NoteClickthrough)
			{
				window.Opacity = 1.0 - (CommonUtils.Settings.NoteTransparency * 0.01);
				window.LeaveMonitor.Stop();
				return;
			}

			var tick = Seconds * CommonUtils.Settings.NoteClickthroughInverse;
			window.Opacity = Lerp(window.StartOpacity, 1.0 - (CommonUtils.Settings.NoteTransparency * 0.01), tick * tick);
		};
	}

	private static void InitMouseMonitor(SearchResult window)
	{
		window.MouseMonitor = new()
		{
			Interval = new TimeSpan(0, 0, 0, 0, 150)
		};

		window.MouseMonitor.Tick += window.WindowMouseMonitor;
	}

	public static void InitMonitors(this SearchResult window)
	{
		InitEnterMonitor(window);
		InitLeaveMonitor(window);
		InitMouseMonitor(window);
	}

	public static void SaveRecord(this SearchResult window)
	{
		if (window.ResultRecord is null)
			return;

		window.ResultDatabase?.CreateRevision(window.ResultRecord, FlowDocumentToXaml(window.ResultBlock.Document));
		window.LastChangedLabel.Content = window.ResultRecord?.GetLastChange();
		DeferUpdateRecentNotes();
	}

	public static bool SetWindowExTransparent(this SearchResult window)
	{
		var extendedStyle = GetWindowLong(window.HWnd, GWL_EXSTYLE);
		return SetWindowLong(window.HWnd, GWL_EXSTYLE, extendedStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT) != 0;
	}

	private static Point Snap(this SearchResult window, ref Point Coords)
	{
		var (XSnapped, YSnapped) = (false, false);

		foreach (SearchResult other in OpenQueries)
		{
			if (XSnapped && YSnapped)
				return Coords;

			if (other.ResultRecord == window.ResultRecord)
				continue;

			Point LT1 = new(Coords.X, Coords.Y);
			Point RB1 = new(Coords.X + window.Width, Coords.Y + window.Height);
			Point LT2 = new(other.Left, other.Top);
			Point RB2 = new(other.Left + other.Width, other.Top + other.Height);

			var dLR = Math.Abs(LT1.X - RB2.X);
			var dRL = Math.Abs(RB1.X - LT2.X);
			var dTB = Math.Abs(LT1.Y - RB2.Y);
			var dBT = Math.Abs(RB1.Y - LT2.Y);

			var dLL = Math.Abs(LT1.X - LT2.X);
			var dRR = Math.Abs(RB1.X - RB2.X);
			var dTT = Math.Abs(LT1.Y - LT2.Y);
			var dBB = Math.Abs(RB1.Y - RB2.Y);

			var XTolerance = (LT1.X >= LT2.X && LT1.X <= RB2.X)
				|| (RB1.X >= LT2.X && RB1.X <= RB2.X)
				|| (LT2.X >= LT1.X && LT2.X <= RB1.X)
				|| (RB2.X >= LT1.X && RB2.X <= RB1.X);

			var YTolerance = (LT1.Y >= LT2.Y && LT1.Y <= RB2.Y)
				|| (RB1.Y >= LT2.Y && RB1.Y <= RB2.Y)
				|| (LT2.Y >= LT1.Y && LT2.Y <= RB1.Y)
				|| (RB2.Y >= LT1.Y && RB2.Y <= RB1.Y);

			if (dLR < window.SnapTolerance && YTolerance && !XSnapped)
			{
				Coords.X = RB2.X;
				XSnapped = true;
			}

			if (dRL < window.SnapTolerance && YTolerance && !XSnapped)
			{
				Coords.X = LT2.X - window.Width;
				XSnapped = true;
			}

			if (dTB < window.SnapTolerance && XTolerance && !YSnapped)
			{
				Coords.Y = RB2.Y;
				YSnapped = true;
			}

			if (dBT < window.SnapTolerance && XTolerance && !YSnapped)
			{
				Coords.Y = LT2.Y - window.Height;
				YSnapped = true;
			}

			if (dLL < window.SnapTolerance && !XSnapped && YSnapped)
			{
				Coords.X = LT2.X;
				(XSnapped, YSnapped) = (true, true);
			}

			if (dRR < window.SnapTolerance && !XSnapped && YSnapped)
			{
				Coords.X = RB2.X - window.Width;
				(XSnapped, YSnapped) = (true, true);
			}

			if (dTT < window.SnapTolerance && XSnapped && !YSnapped)
			{
				Coords.Y = LT2.Y;
				(XSnapped, YSnapped) = (true, true);
			}

			if (dBB < window.SnapTolerance && XSnapped && !YSnapped)
			{
				Coords.Y = RB2.Y - window.Height;
				(XSnapped, YSnapped) = (true, true);
			}
		}

		return Coords;
	}

	public static void StopMonitors(this SearchResult window)
	{
		window.EnterMonitor?.Stop();
		window.LeaveMonitor?.Stop();
		window.MouseMonitor?.Stop();
	}
	public static bool UnsetWindowExTransparent(this SearchResult window)
	{
		int extendedStyle = GetWindowLong(window.HWnd, GWL_EXSTYLE);
		return SetWindowLong(window.HWnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_LAYERED & ~WS_EX_TRANSPARENT) != 0;
	}
}
