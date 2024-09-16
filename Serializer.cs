using System;
using System.Collections;
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
		private static StreamReader? fileReader;
		private static StreamWriter? fileWriter;
		private static bool _isOpen = false;
		private static List<byte> _lzwBytes = [];
		private readonly static Dictionary<uint, string> _lzwCodeMap = [];
		private readonly static Dictionary<string, uint> _lzwPacketMap = [];
		private static uint _lzwNextCode = 0U;
		private static uint _lzwRange = 0U;
		private readonly static List<byte> _stringBuffer = [];
		private static bool _writing = false;

		public static void Close()
		{
			if (_isOpen)
			{
				if (_writing)
					fileWriter?.Flush();

				fileWriter?.Close();
				fileReader?.Close();
			}

			_isOpen = false;
		}

		private static string FromByte(byte b)
		{
			var sb = (char)b;
			return new([sb]);
		}

		private static void LZWAppend(uint code)
		{
			for (uint i = 1; i <= _lzwRange; i++)
			{
				var shift = code >> (byte)(_lzwRange - i);
				_lzwBytes.Add((byte)(shift & 1U));
			}
		}

		private static void LZWCompressToFile(byte[] data)
		{
			_lzwBytes.Clear();
			LZWInit();

			string p = FromByte(data[0]);
			for (int byteIndex = 0; byteIndex < data.Length; byteIndex++)
			{
				string c = FromByte(data[byteIndex]);
				if (_lzwPacketMap.ContainsKey($"{p}{c}"))
					p = $"{p}{c}";
				else
				{
					var code = _lzwPacketMap[p];
					LZWAppend(code);

					_lzwPacketMap[$"{p}{c}"] = _lzwNextCode++;
					p = c;

					_lzwRange = _lzwNextCode switch
					{
						512U => 10U,
						1024U => 11U,
						2048U => 12U,
						4096U => 13U,
						_ => _lzwRange,
					};
					if (_lzwRange >= 13U)
						LZWInit();
				}
			}

			LZWAppend(_lzwPacketMap[p]);
			LZWAppend(257U);

			while (_lzwBytes.Count % 8 != 0)
				_lzwBytes.Add(0);

			List<byte[]>? _broken = _lzwBytes.Chunk(8).ToList();
			for (int i = 0; i < _broken.Count; i++)
			{
				byte b = 0;
				for (uint j = 0; j < 8; j++)
					b += (byte)(_broken[i][j] << (byte)j);
				
			}
		}

		private static byte[] LZWDecompress(byte[] data)
		{
			int bitIndex = 0;
			BitArray bits = new(data);

			_lzwBytes.Clear();
			LZWInit();

			uint oldCode = LZWReadCode(ref bits, ref bitIndex);
			if (oldCode == 257U)
				return data;

			string? s = _lzwCodeMap[oldCode];

			while (bitIndex < bits.Length)
			{
				uint k = LZWReadCode(ref bits, ref bitIndex);

				if (!_lzwCodeMap.TryGetValue(k, out string? c))
					c = $"{s}{s[0]}";

				for (int i = 0; i < c.Length; i++)
					_lzwBytes = [.. _lzwBytes, (byte)c[i]];

				_lzwCodeMap.Add(_lzwNextCode++, $"{s}{c[0]}");
				s = c;

				_lzwRange = _lzwNextCode switch
				{
					511U => 10U,
					1023U => 11U,
					2047U => 12U,
					4095U => 13U,
					_ => _lzwRange,
				};
				if (_lzwRange >= 13U)
					LZWInit();
			}

			return [.. _lzwBytes];
		}

		private static void LZWInit()
		{
			_lzwCodeMap.Clear();
			_lzwNextCode = 258U;
			_lzwPacketMap.Clear();
			_lzwRange = 9U;

			for (uint i = 0; i < 256; i++)
				_lzwCodeMap[i] = FromByte((byte)i);

			for (uint i = 0; i < 256; i++)
			{
				var bi = (byte)i;
				var si = FromByte(bi);
				_lzwPacketMap[si] = i;
			}
		}

		private static uint LZWReadCode(ref BitArray bits, ref int bitIndex)
		{
			uint _code = 0;
			for (uint i = 1; i <= _lzwRange; i++)
			{
				if (bitIndex >= bits.Length)
					return 257U;
				var bi = bits.Get(bitIndex++) ? 1U : 0U;
				_code += bi << (byte)(_lzwRange - i);
			}
			return _code;
		}

		public static bool OpenRead(string path)
		{
			Close();

			try
			{
				_fileBuffer = LZWDecompress(File.ReadAllBytes(path));
				_dataStream = new MemoryStream(_fileBuffer, false);
				_isOpen = true;
				_writing = false;
			}
			catch (Exception e)
			{
				MessageBox.Show($"WARNING: Failed to access {path} - {e.Message}", "Sylver Ink: Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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

				_dataStream = new FileStream(path, FileMode.Create);
				_isOpen = true;
				_stringBuffer.Clear();
				_writing = true;
			}
			catch (Exception e)
			{
				var result = MessageBox.Show($"WARNING: Failed to access {path} - {e.Message}.\nProceed with exiting?", "Sylver Ink: Error", MessageBoxButton.YesNo, MessageBoxImage.Error);
				Common.ForceClose = result == MessageBoxResult.Yes;
				return false;
			}

			return true;
		}

		public static int ReadInt32(ref int item)
		{
			if (!_isOpen)
				return item;

			uint nextSize = ReadUInt32();
			if (nextSize == 0)
				return item;

			try
			{
				_buffer = new byte[nextSize];
				_dataStream?.Read(_buffer, 0, (int)nextSize);
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

			uint nextSize = ReadUInt32();
			if (nextSize == 0)
				return item;

			try
			{
				_buffer = new byte[nextSize];
				_dataStream?.Read(_buffer, 0, (int)nextSize);
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

			uint nextSize = ReadUInt32();
			if (nextSize == 0)
				return item;

			try
			{
				_buffer = new byte[nextSize];
				_dataStream?.Read(_buffer, 0, (int)nextSize);
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
			if (!_isOpen)
				return 0;

			_buffer = new byte[4];
			if (_dataStream?.Read(_buffer, 0, 4) == 4)
				return _buffer[0] * 16777216U
					+ _buffer[1] * 65536U
					+ _buffer[2] * 256U
					+ _buffer[3];

			return 0;
		}

		public static void WriteInt32(int value)
		{
			if (!_isOpen)
				return;

			_buffer = Encoding.UTF8.GetBytes(value.ToString());

			WriteUInt32((uint)_buffer.Length);
			for (uint i = 0; i < _buffer.Length; i++)
				_stringBuffer.Add(_buffer[i]);
		}

		public static void WriteLong(long value)
		{
			if (!_isOpen)
				return;

			_buffer = Encoding.UTF8.GetBytes(value.ToString());

			WriteUInt32((uint)_buffer.Length);
			for (uint i = 0; i < _buffer.Length; i++)
				_stringBuffer.Add(_buffer[i]);
		}

		public static void WriteString(string? value)
		{
			if (!_isOpen)
				return;

			_buffer = Encoding.UTF8.GetBytes(value ?? string.Empty);

			WriteUInt32((uint)_buffer.Length);
			for (uint i = 0; i < _buffer.Length; i++)
				_stringBuffer.Add(_buffer[i]);
		}

		private static void WriteUInt32(uint data)
		{
			if (!_isOpen)
				return;

			byte[] translate = [
				(byte)((data >> 24) & 0xFF),
				(byte)((data >> 16) & 0xFF),
				(byte)((data >> 8) & 0xFF),
				(byte)(data & 0xFF),
			];

			for (int i = 0; i < translate.Length; i++)
				_stringBuffer.Add(translate[i]);
		}
	}
}
