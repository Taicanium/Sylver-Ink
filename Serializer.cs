using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;

namespace SylverInk
{
	static class Serializer
	{
		private static byte[] _buffer = [];
		private static Stream? _fileStream;
		private static bool _isOpen = false;
		private static bool _lzw = false;
		private static List<byte> _outgoing = [];
		private static byte[] _testBuffer = [];
		private static bool _writing = false;

		/// <summary>
		/// <para>
		/// Format 1: Uncompressed.
		/// Data is serialized to plaintext.
		/// Four bytes are written indicating the object's length in string format, followed by that formatted string.
		/// </para>
		/// <para>
		/// Format 2: LZW Restricted.
		/// Data is compressed using the Lempel–Ziv–Welch (LZW) algorithm.
		/// The LZW bit stream is formatted with a code dictionary at most 25 bits wide, yielding a length at most 2^25, or ~33.5 million.
		/// The dictionary is not reset when its width limit is reached. This results in exponential resource usage with increasing database size, eventually becoming prohibitive.
		/// </para>
		/// <para>
		/// Format 3: LZW Unrestricted.
		/// Not yet implemented.
		/// When implemented, this format will compress data with an LZW dictionary that resets once its width limit is reached, preventing runaway resource usage and allowing for much larger databases.
		/// </para>
		/// </summary>
		public static byte DatabaseFormat { get; set; } = 2;

		/// <summary>
		/// Begin testing if the current database can be compressed in format 2.
		/// </summary>
		public static void BeginCompressionTest()
		{
			Close();

			_fileStream = new MemoryStream();
			_isOpen = true;
			_writing = true;

			WriteHeader(2);
		}

		public static void ClearCompressionTest()
		{
			Close();

			_fileStream = null;
			_outgoing = [];
			_testBuffer = [];
		}

		public static void Close(bool testing = false)
		{
			if (_isOpen)
			{
				LZW.Close();
				if (_writing)
				{
					if (_lzw)
						_outgoing = LZW.Outgoing;
					_fileStream?.Write([.. _outgoing], 0, _outgoing.Count);
					_fileStream?.Flush();
				}

				if (!testing)
					_fileStream?.Dispose();
			}

			_isOpen = false;
		}

		public static void EndCompressionTest()
		{
			Close(true);

			var _memoryStream = _fileStream as MemoryStream;
			_testBuffer = _memoryStream?.ToArray() ?? [];
			_fileStream = new MemoryStream(_testBuffer, false);
			_isOpen = true;
			_writing = false;

			ReadHeader();
		}

		public static bool OpenRead(string path)
		{
			Close();

			try
			{
				_fileStream = new FileStream(path, FileMode.Open);
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

				_fileStream = new FileStream(path, FileMode.Create);
				_isOpen = true;
				_writing = true;

				WriteHeader(DatabaseFormat);
			}
			catch (Exception e)
			{
				var result = MessageBox.Show($"WARNING: Failed to access {path} - {e.Message}\n\nProceed with exiting?", "Sylver Ink: Error", MessageBoxButton.YesNo, MessageBoxImage.Error);
				Common.ForceClose = result == MessageBoxResult.Yes;
				return false;
			}

			return true;
		}

		private static byte ReadByte()
		{
			if (_lzw)
			{
				var b = LZW.Decompress();
				if (b.Length > 0)
					return b[0];
				return 0;
			}

			return (byte)(_fileStream?.ReadByte() ?? 0);
		}

		private static byte[] ReadBytes(int byteCount)
		{
			if (_lzw)
			{
				var b = LZW.Decompress(byteCount);
				return b;
			}

			_buffer = new byte[byteCount];
			_fileStream?.Read(_buffer, 0, byteCount);
			return _buffer;
		}

		private static void ReadHeader()
		{
			_buffer = new byte[5];
			_fileStream?.Read(_buffer, 0, 5);

			string header = Encoding.UTF8.GetString(_buffer);
			DatabaseFormat = (byte)header[^1];

			switch (DatabaseFormat)
			{
				case 1:
					_lzw = false;
					break;
				case 2:
					LZW.Init(_fileStream);
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
				_buffer = ReadBytes(nextSize);
				item = int.Parse(Encoding.UTF8.GetString(_buffer));
				return item;
			}
			catch (Exception)
			{
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
				_buffer = ReadBytes(nextSize);
				item = long.Parse(Encoding.UTF8.GetString(_buffer));
				return item;
			}
			catch (Exception)
			{
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
				_buffer = ReadBytes(nextSize);
				item = Encoding.UTF8.GetString(_buffer);
				return item;
			}
			catch (Exception)
			{
				return item;
			}
		}

		private static uint ReadUInt32()
		{
			return ReadByte() * 16777216U
			+ ReadByte() * 65536U
			+ ReadByte() * 256U
			+ ReadByte();
		}

		private static void WriteBytes(byte[] data)
		{
			if (_lzw)
				LZW.Compress(data);
			_outgoing.AddRange(data);
		}

		private static void WriteHeader(byte format)
		{
			_fileStream?.Write(Encoding.UTF8.GetBytes(
				$"SYL {(char)format}"
			));

			if (format == 2)
			{
				LZW.Init(_fileStream, true);
				_lzw = true;
			}
		}

		public static void WriteInt32(int value)
		{
			if (!_isOpen)
				return;

			_buffer = Encoding.UTF8.GetBytes(value.ToString());
			WriteUInt32((uint)_buffer.Length);
			WriteBytes(_buffer);
		}

		public static void WriteLong(long value)
		{
			if (!_isOpen)
				return;

			_buffer = Encoding.UTF8.GetBytes(value.ToString());
			WriteUInt32((uint)_buffer.Length);
			WriteBytes(_buffer);
		}

		public static void WriteString(string? value)
		{
			if (!_isOpen)
				return;

			_buffer = Encoding.UTF8.GetBytes(value ?? string.Empty);
			WriteUInt32((uint)_buffer.Length);
			WriteBytes(_buffer);
		}

		private static void WriteUInt32(uint data)
		{
			WriteBytes([
				(byte)((data >> 24) & 0xFF),
				(byte)((data >> 16) & 0xFF),
				(byte)((data >> 8) & 0xFF),
				(byte)(data & 0xFF)
			]);
		}
	}
}
