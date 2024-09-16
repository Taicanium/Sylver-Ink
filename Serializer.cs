using System;
using System.IO;
using System.Text;
using System.Windows;

namespace SylverInk
{
	static class Serializer
	{
		private static byte[] _buffer = [];
		private static FileStream? _fileStream;
		private static bool _isOpen = false;
		private static bool _lzw = false;
		private static bool _writing = false;

		public static void Close()
		{
			if (_isOpen)
			{
				if (_writing)
					_fileStream?.Flush();
				_fileStream?.Dispose();
			}

			_isOpen = false;
		}

		public static bool OpenRead(string path)
		{
			Close();

			try
			{
				_fileStream = new(path, FileMode.Open);
				_isOpen = true;
				_writing = false;

				ReadHeader();
			}
			catch (Exception e)
			{
				MessageBox.Show($"WARNING: Failed to access {path} - {e.Message}", "Sylver Ink: Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
				return false;
			}

			return true;
		}

		public static bool OpenWrite(string path)
		{
			Close();

			try
			{
				if (File.Exists(path))
					File.Delete(path);

				_fileStream = new(path, FileMode.Create);
				_isOpen = true;
				_writing = true;

				WriteHeader(Common.DatabaseFormat);
			}
			catch (Exception e)
			{
				var result = MessageBox.Show($"WARNING: Failed to access {path} - {e.Message}.\nProceed with exiting?", "Sylver Ink: Error", MessageBoxButton.YesNo, MessageBoxImage.Error);
				Common.ForceClose = result == MessageBoxResult.Yes;
				return false;
			}

			return true;
		}

		private static void ReadHeader()
		{
			_buffer = new byte[5];
			_fileStream?.Read(_buffer, 0, 5);

			string header = Encoding.UTF8.GetString(_buffer);
			string magic = header[..4];
			Common.DatabaseFormat = (byte)header[^1];

			switch (Common.DatabaseFormat)
			{
				case 1:
					_lzw = false;
					break;
				case 2:
					_lzw = true;
					break;
			}
		}

		public static int ReadInt32(ref int item)
		{
			if (!_isOpen)
				return item;

			int nextSize = (int)ReadUInt32();
			if (nextSize == 0)
				return item;

			try
			{
				_buffer = new byte[nextSize];
				_fileStream?.Read(_buffer, 0, nextSize);
				item = int.Parse(Encoding.UTF8.GetString(_buffer));
				return item;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "Sylver Ink: Error", MessageBoxButton.OK);
				return item;
			}
		}

		public static long ReadLong(ref long item)
		{
			if (!_isOpen)
				return item;

			int nextSize = (int)ReadUInt32();
			if (nextSize == 0)
				return item;

			try
			{
				_buffer = new byte[nextSize];
				_fileStream?.Read(_buffer, 0, nextSize);
				item = long.Parse(Encoding.UTF8.GetString(_buffer));
				return item;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "Sylver Ink: Error", MessageBoxButton.OK);
				return item;
			}
		}

		public static string? ReadString(ref string? item)
		{
			if (!_isOpen)
				return item;

			int nextSize = (int)ReadUInt32();
			if (nextSize == 0)
				return item;

			try
			{
				_buffer = new byte[nextSize];
				_fileStream?.Read(_buffer, 0, nextSize);
				item = Encoding.UTF8.GetString(_buffer);
				return item;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "Sylver Ink: Error", MessageBoxButton.OK);
				return item;
			}
		}

		private static uint ReadUInt32()
		{
			return (uint?)(_fileStream?.ReadByte() * 16777216U
				+ _fileStream?.ReadByte() * 65536U
				+ _fileStream?.ReadByte() * 256U
				+ _fileStream?.ReadByte()) ?? 0U;
		}

		private static void WriteHeader(byte format)
		{
			_fileStream?.Write(Encoding.UTF8.GetBytes(
				$"SYL {(char)format}"
			));
		}

		public static void WriteInt32(int value)
		{
			if (!_isOpen)
				return;

			_buffer = Encoding.UTF8.GetBytes(value.ToString());
			WriteUInt32((uint)_buffer.Length);
			_fileStream?.Write(_buffer);
		}

		public static void WriteLong(long value)
		{
			if (!_isOpen)
				return;

			_buffer = Encoding.UTF8.GetBytes(value.ToString());
			WriteUInt32((uint)_buffer.Length);
			_fileStream?.Write(_buffer);
		}

		public static void WriteString(string? value)
		{
			if (!_isOpen)
				return;

			_buffer = Encoding.UTF8.GetBytes(value ?? string.Empty);
			WriteUInt32((uint)_buffer.Length);
			_fileStream?.Write(_buffer);
		}

		private static void WriteUInt32(uint data)
		{
			_fileStream?.WriteByte((byte)((data >> 24) & 0xFF));
			_fileStream?.WriteByte((byte)((data >> 16) & 0xFF));
			_fileStream?.WriteByte((byte)((data >> 8) & 0xFF));
			_fileStream?.WriteByte((byte)(data & 0xFF));
		}
	}
}
