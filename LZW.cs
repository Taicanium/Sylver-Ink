using System;
using System.Collections.Generic;
using System.IO;

namespace SylverInk
{
	static class LZW
    {
		private static string? _lzwC = string.Empty;
		private readonly static List<byte> _lzwBitStream = [];
		private readonly static Dictionary<string, uint> _lzwCodes = [];
		private readonly static int _lzwMaxRange = 24;
		private static uint _lzwNextCode = 258U;
		private static bool _lzwOpen = false;
		private readonly static Dictionary<uint, string> _lzwPackets = [];
		private static int _lzwRange = 9;
		private static string _lzwW = string.Empty;

		private static string FromByte(byte c) => $"{(char)c}";

		private static string FromChar(char c) => $"{c}";

		public static Stream? FileStream;
		public static List<byte> Incoming = [];
		public static List<byte> Outgoing = [];
		public static bool Writing = false;

		public static void Close()
		{
			if (!_lzwOpen || !Writing)
			{
				_lzwW = string.Empty;
				_lzwC = string.Empty;

				_lzwBitStream.Clear();
				return;
			}

			if (!_lzwW.Equals(string.Empty)) // If there's still one or two more letters to write...
				WriteCode(_lzwCodes[_lzwW]); // Write them.

			WriteCode(257U);
			WriteCode(0U);

			byte[] bits = [.. _lzwBitStream];
			var bitSize = bits.Length;
			byte b = 0;
			int j = 1;
			for (int i = 0; i < bitSize; i++)
			{
				b += (byte)(bits[i] << (8 - j));
				j++;
				if (j == 9)
				{
					j = 1;
					Outgoing.Add(b);
					b = 0;
				}
			}

			_lzwBitStream.Clear();
			_lzwC = string.Empty;
			_lzwOpen = false;
			_lzwW = string.Empty;
		}

		public static void Compress(byte[] data)
		{
			for (int i = 0; i < data.Length; i++)
			{
				_lzwC = FromByte(data[i]);
				string wc = _lzwW + _lzwC;
				if (!_lzwCodes.TryAdd(wc, _lzwNextCode))
					_lzwW = wc;
				else
				{
					var entry = _lzwCodes[_lzwW];
					WriteCode(entry);
					_lzwNextCode++;
					_lzwW = _lzwC;

					UpdateRange(_lzwNextCode);
				}
			}
		}

		public static byte[] Decompress(int byteCount = 1)
		{
			if (_lzwW.Equals(string.Empty))
			{
				var k = ReadCode();
				if (k != 257U)
				{
					_lzwW = _lzwPackets[k];
					for (int i = 0; i < _lzwW.Length; i++)
						Incoming.Add((byte)_lzwW[i]);
				}
			}

			while (Incoming.Count == 0 || Incoming.Count < byteCount)
			{
				var k = ReadCode();
				if (k == 256U)
					Init();
				else if (k == 257U)
					break;
				else
				{
					if (!_lzwPackets.TryGetValue(k, out _lzwC))
						_lzwC = $"{_lzwW}{_lzwW[0]}";

					for (int i = 0; i < _lzwC.Length; i++)
						Incoming.Add((byte)_lzwC[i]);

					_lzwPackets.Add(_lzwNextCode, $"{_lzwW}{_lzwC[0]}");
					_lzwW = _lzwC;
					_lzwNextCode++;

					UpdateRange(_lzwNextCode + 1);
				}
			}

			var limit = Math.Min(byteCount, Incoming.Count);
			var range = Incoming[..limit];
			Incoming.RemoveRange(0, limit);
			return [.. range];
		}

		public static void Init(Stream? _fileStream = null, bool _writing = false)
		{
			FileStream = _fileStream ?? FileStream;
			Writing = _writing;
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

		private static uint ReadCode()
		{
			uint code = 0;

			while (_lzwBitStream.Count < _lzwRange)
			{
				var n = FileStream?.ReadByte();
				if (n == -1)
					return 257U;

				byte b = (byte)(n ?? 0);
				for (int i = 1; i <= 8; i++)
					_lzwBitStream.Add((byte)((b >> (8 - i)) & 1));
			}

			for (int i = 1; i <= _lzwRange; i++)
				code += (uint)(_lzwBitStream[i - 1] << (_lzwRange - i));
			_lzwBitStream.RemoveRange(0, _lzwRange);

			return code;
		}

		private static void UpdateRange(uint lastCode)
		{
			for (int i = _lzwRange; i <= _lzwMaxRange; i++)
			{
				if (lastCode >= (uint)Math.Pow(2, i))
					_lzwRange = i + 1;
				else
					break;
			}

			if (lastCode >= (uint)Math.Pow(2, _lzwMaxRange + 1))
				throw new ApplicationException("Serialized database has exceeded the maximum capacity for restricted LZW compression.", new IndexOutOfRangeException());
		}

		private static void WriteCode(uint code)
		{
			for (int i = 1; i <= _lzwRange; i++)
				_lzwBitStream.Add((byte)((code >> (_lzwRange - i)) & 1));
		}
	}
}
