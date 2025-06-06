using SylverInk.XAML;
using System;
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
	public static void AddTabToRibbon(this SearchResult window)
	{
		if (window.ResultRecord is null)
			return;

		if (window.ResultDatabase is null)
			return;

		SwitchDatabase(window.ResultDatabase);

		TabItem item = new()
		{
			Content = new NoteTab() { Record = window.ResultRecord },
			Header = GetRibbonHeader(window.ResultRecord)
		};

		var ChildPanel = GetChildPanel("DatabasesPanel");
		ChildPanel.SelectedIndex = ChildPanel.Items.Add(item);
		OpenTabs.Add(item);

		window.Close();
	}

	public static void Autosave(this SearchResult window)
	{
		var lockFile = GetLockFile();
		Erase(lockFile);
		CurrentDatabase.Save(lockFile);
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

	public static void SaveRecord(this SearchResult window)
	{
		if (window.ResultRecord is null)
			return;

		window.ResultText = FlowDocumentToXaml(window.ResultBlock.Document);
		window.ResultDatabase?.CreateRevision(window.ResultRecord, window.ResultText);
		window.LastChangedLabel.Content = window.ResultRecord?.GetLastChange();
		DeferUpdateRecentNotes();
	}

	private static Point Snap(this SearchResult window, ref Point Coords)
	{
		var Snapped = (false, false);

		foreach (SearchResult other in OpenQueries)
		{
			if (Snapped.Item1 && Snapped.Item2)
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

			if (dLR < window.SnapTolerance && YTolerance && !Snapped.Item1)
			{
				Coords.X = RB2.X;
				Snapped = (true, Snapped.Item2);
			}

			if (dRL < window.SnapTolerance && YTolerance && !Snapped.Item1)
			{
				Coords.X = LT2.X - window.Width;
				Snapped = (true, Snapped.Item2);
			}

			if (dTB < window.SnapTolerance && XTolerance && !Snapped.Item2)
			{
				Coords.Y = RB2.Y;
				Snapped = (Snapped.Item1, true);
			}

			if (dBT < window.SnapTolerance && XTolerance && !Snapped.Item2)
			{
				Coords.Y = LT2.Y - window.Height;
				Snapped = (Snapped.Item1, true);
			}

			if (dLL < window.SnapTolerance && !Snapped.Item1 && Snapped.Item2)
			{
				Coords.X = LT2.X;
				Snapped = (true, true);
			}

			if (dRR < window.SnapTolerance && !Snapped.Item1 && Snapped.Item2)
			{
				Coords.X = RB2.X - window.Width;
				Snapped = (true, true);
			}

			if (dTT < window.SnapTolerance && Snapped.Item1 && !Snapped.Item2)
			{
				Coords.Y = LT2.Y;
				Snapped = (true, true);
			}

			if (dBB < window.SnapTolerance && Snapped.Item1 && !Snapped.Item2)
			{
				Coords.Y = RB2.Y - window.Height;
				Snapped = (true, true);
			}
		}

		return Coords;
	}
}
