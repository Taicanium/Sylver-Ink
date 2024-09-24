using System;
using System.Collections.Generic;
using System.IO;

namespace SylverInk
{
	public partial class LZW
	{
		private string? C = string.Empty;
		private List<byte> BitStream { get; } = [];
		private Dictionary<string, uint> Codes { get; } = [];

		private Stream? FileStream;
		private List<byte> Incoming { get; } = [];
		private int MaxRange { get; } = 24;
		private uint NextCode = 258U;
		private bool Open = false;
		public List<byte> Outgoing { get; } = [];
		private Dictionary<uint, string> Packets { get; } = [];
		private int Range = 9;
		private string W = string.Empty;
		private bool Writing = false;

		public void Close()
		{
			if (Open && Writing)
			{
				if (!W.Equals(string.Empty))
					WriteCode(Codes[W]);

				WriteCode(257U);
				WriteCode(0U);

				byte[] bits = [.. BitStream];
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
			}

			BitStream.Clear();
			C = string.Empty;
			Codes.Clear();
			Open = false;
			Packets.Clear();
			W = string.Empty;
			Writing = false;
		}

		public void Compress(byte[] data)
		{
			for (int i = 0; i < data.Length; i++)
			{
				C = FromByte(data[i]);
				string wc = W + C;
				if (!Codes.TryAdd(wc, NextCode))
					W = wc;
				else
				{
					var entry = Codes[W];
					WriteCode(entry);
					NextCode++;
					W = C;

					UpdateRange(NextCode);
				}
			}
		}

		public byte[] Decompress(int byteCount = 1)
		{
			if (W.Equals(string.Empty))
			{
				var k = ReadCode();
				if (k != 257U)
				{
					W = Packets[k];
					for (int i = 0; i < W.Length; i++)
						Incoming.Add((byte)W[i]);
				}
			}

			while (Incoming.Count == 0 || Incoming.Count < byteCount)
			{
				var k = ReadCode();
				if (k == 257U)
					break;
				else
				{
					if (!Packets.TryGetValue(k, out C))
						C = $"{W}{W[0]}";

					for (int i = 0; i < C.Length; i++)
						Incoming.Add((byte)C[i]);

					Packets.Add(NextCode, $"{W}{C[0]}");
					NextCode++;
					W = C;

					UpdateRange(NextCode + 1);
				}
			}

			var limit = Math.Min(byteCount, Incoming.Count);
			var range = Incoming[..limit];
			Incoming.RemoveRange(0, limit);
			return [.. range];
		}

		private static string FromByte(byte c) => $"{(char)c}";

		private static string FromChar(char c) => $"{c}";

		public void Init(Stream? _fileStream = null, bool _writing = false)
		{
			FileStream = _fileStream ?? FileStream;
			Writing = _writing;
			InitDictionary();

			Open = true;
		}

		private void InitDictionary()
		{
			Codes.Clear();
			NextCode = 258U;
			Packets.Clear();
			Range = 9;

			for (char i = (char)0; i < 256; i++)
			{
				Codes[FromChar(i)] = i;
				Packets[i] = FromChar(i);
			}
		}

		private uint ReadCode()
		{
			uint code = 0;

			while (BitStream.Count < Range)
			{
				var n = FileStream?.ReadByte();
				if (n == -1)
					return 257U;

				byte b = (byte)(n ?? 0);
				for (int i = 1; i <= 8; i++)
					BitStream.Add((byte)((b >> (8 - i)) & 1));
			}

			for (int i = 1; i <= Range; i++)
				code += (uint)(BitStream[i - 1] << (Range - i));
			BitStream.RemoveRange(0, Range);

			return code;
		}

		private void UpdateRange(uint lastCode)
		{
			for (int i = Range; i <= MaxRange; i++)
			{
				if (lastCode >= (uint)Math.Pow(2, i))
					Range = i + 1;
				else
					break;
			}

			if (lastCode >= (uint)Math.Pow(2, MaxRange + 1))
				throw new ApplicationException("Serialized database has exceeded the maximum capacity for restricted LZW compression.", new IndexOutOfRangeException());
		}

		private void WriteCode(uint code)
		{
			for (int i = 1; i <= Range; i++)
				BitStream.Add((byte)((code >> (Range - i)) & 1));
		}
	}
}
