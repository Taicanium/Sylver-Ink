using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
		private static string _lzwC = string.Empty;
		private readonly static List<byte> _lzwBitStream = [];
		private readonly static Dictionary<string, uint> _lzwCodes = [];
		private readonly static List<byte> _incoming = [];
		private static uint _lzwNextCode = 258U;
		private static bool _lzwOpen = false;
		private readonly static List<byte> _outgoing = [];
		private readonly static Dictionary<uint, string> _lzwPackets = [];
		private static int _lzwRange = 9;
		private static string _lzwW = string.Empty;
		private static bool _writing = false;

		public static void Close()
		{
			if (_isOpen)
			{
				if (_writing)
				{
					if (_lzw)
						LZWClose();
					_fileStream?.Write([.. _outgoing], 0, _outgoing.Count);
					_outgoing.Clear();
					_fileStream?.Flush();
				}
				_fileStream?.Dispose();
			}

			_isOpen = false;
		}

		private static string FromByte(byte c) => $"{(char)c}";

		private static string FromChar(char c) => $"{c}";

		private static void LZWClose()
		{
			if (!_lzwOpen)
				return;

			if (!_lzwW.Equals(string.Empty))
				LZWWriteCode(_lzwCodes[_lzwW]);

			while (_lzwBitStream.Count % 8 != 0)
				_lzwBitStream.Add(0);

			var chunks = _lzwBitStream.Chunk(8);
			for (int i = 0; i < chunks.Count(); i++)
			{
				var chunk = chunks.ElementAt(i);
				byte b = 0;
				for (byte j = 1; j <= 8; j++)
					b += (byte)(chunk[j - 1] << (8 - j));
				_outgoing.Add(b);
			}

			_lzwBitStream.Clear();
			_lzwOpen = false;
		}

		private static void LZWCompress(byte[] data)
		{
			for (int i = 0; i < data.Length; i++)
			{
				_lzwC = FromByte(data[i]);
				string wc = _lzwW + _lzwC;
				if (_lzwCodes.ContainsKey(wc))
					_lzwW = wc;
				else
				{
					LZWWriteCode(_lzwCodes[_lzwW]);
					_lzwCodes.Add(wc, _lzwNextCode++);
					_lzwW = _lzwC;

					_lzwRange = _lzwNextCode switch
					{
						512U => 10,
						1024U => 11,
						2048U => 12,
						4096U => 13,
						_ => _lzwRange,
					};

					if (_lzwRange >= 13)
						LZWReinit();
				}
			}
		}

		private static byte[] LZWDecompress(int byteCount = 1)
		{
			if (_lzwW.Equals(string.Empty))
				_lzwW = _lzwPackets[LZWReadCode()];

			while (_incoming.Count < byteCount)
			{
				string? entry = string.Empty;
				var k = LZWReadCode();
				if (!_lzwPackets.TryGetValue(k, out entry))
					entry = $"{_lzwW}{_lzwW[0]}";

				for (int i = 0; i < entry.Length; i++)
					_incoming.Add((byte)entry[i]);

				_lzwPackets.Add(_lzwNextCode++, $"{_lzwW}{entry[0]}");
				_lzwW = entry;

				_lzwRange = _lzwNextCode switch
				{
					511U => 10,
					1023U => 11,
					2047U => 12,
					4095U => 13,
					_ => _lzwRange,
				};

				if (_lzwRange >= 13)
					LZWReinit();
			}

			var range = _incoming[..byteCount];
			_incoming.RemoveRange(0, byteCount);
			return [.. range];
		}

		private static void LZWInit()
		{
			_lzwCodes.Clear();
			_lzwNextCode = 258U;
			_lzwPackets.Clear();
			_lzwRange = 9;

			for (char i = (char)0; i < 256; i++)
			{
				_lzwCodes[FromChar(i)] = i;
				_lzwPackets[i] = FromChar(i);
			}

			_lzwOpen = true;
		}

		private static uint LZWReadCode()
		{
			uint code = 0;

			while (_lzwBitStream.Count < _lzwRange)
			{
				byte b = (byte)(_fileStream?.ReadByte() ?? 0);
				for (int i = 1; i <= 8; i++)
					_lzwBitStream.Add((byte)((b >> (8 - i)) & 1));
			}

			for (int i = 1; i <= _lzwRange; i++)
				code += (uint)(_lzwBitStream[i - 1] << (_lzwRange - i));
			_lzwBitStream.RemoveRange(0, _lzwRange);

			return code;
		}

		private static void LZWReinit()
		{
			LZWClose();
			LZWInit();
		}

		private static void LZWWriteCode(uint code)
		{
			for (int i = 1; i <= _lzwRange; i++)
				_lzwBitStream.Add((byte)((code >> (_lzwRange - i)) & 1));
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

		private static byte ReadByte()
		{
			if (_lzw)
			{
				var b = LZWDecompress()[0];
				return b;
			}

			return (byte)(_fileStream?.ReadByte() ?? 0);
		}

		private static byte[] ReadBytes(int byteCount)
		{
			if (_lzw)
			{
				var b = LZWDecompress(byteCount);
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
			string magic = header[..4];
			Common.DatabaseFormat = (byte)header[^1];

			switch (Common.DatabaseFormat)
			{
				case 1:
					_lzw = false;
					break;
				case 2:
					LZWInit();
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
				_buffer = ReadBytes(nextSize);
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
				_buffer = ReadBytes(nextSize);
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
			if (_lzw)
			{
				return ReadByte() * 16777216U
				+ ReadByte() * 65536U
				+ ReadByte() * 256U
				+ ReadByte();
			}

			return (uint?)(_fileStream?.ReadByte() * 16777216U
				+ _fileStream?.ReadByte() * 65536U
				+ _fileStream?.ReadByte() * 256U
				+ _fileStream?.ReadByte()) ?? 0U;
		}

		private static void WriteBytes(byte data)
		{
			if (_lzw)
			{
				LZWCompress([data]);
				return;
			}
			_outgoing.Add(data);
		}

		private static void WriteBytes(byte[] data)
		{
			if (_lzw)
			{
				LZWCompress(data);
				return;
			}
			_outgoing.AddRange(data);
		}

		private static void WriteHeader(byte format)
		{
			_fileStream?.Write(Encoding.UTF8.GetBytes(
				$"SYL {(char)format}"
			));

			if (format == 2)
			{
				LZWInit();
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
			WriteBytes((byte)((data >> 24) & 0xFF));
			WriteBytes((byte)((data >> 16) & 0xFF));
			WriteBytes((byte)((data >> 8) & 0xFF));
			WriteBytes((byte)(data & 0xFF));
		}
	}
}
