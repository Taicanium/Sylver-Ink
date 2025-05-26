using System;
using System.Collections.Generic;
using System.IO;

namespace SylverInk.FileIO;

public partial class LZW
{
	private string? C = string.Empty;
	private List<byte> BitStream { get; } = [];
	private Dictionary<string, uint> Codes { get; } = [];

	private Stream? FileStream;
	private List<byte> Incoming { get; } = [];
	private int MaxRange { get; } = 24;
	private uint NextCode = 258U;
	private bool Open;
	public List<byte> Outgoing { get; } = [];
	private Dictionary<uint, string> Packets { get; } = [];
	private int Range = 9;
	private string W = string.Empty;
	private bool Writing;

	/// <summary>
	/// Resets the LZW state engine.
	/// </summary>
	public void Close()
	{
		if (Open && Writing)
		{
			if (!string.IsNullOrEmpty(W))
				WriteCode(Codes[W]);

			WriteCode(257U);
			WriteCode(0U);

			byte[] bits = [.. BitStream];
			var bitSize = bits.Length;
			byte b = 0;
			int j = 1;
			for (int i = 0; i < bitSize; i++)
			{
				b += (byte)(bits[i] << 8 - j);
				if (j++ != 8)
					continue;

				j = 1;
				Outgoing.Add(b);
				b = 0;
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

	/// <summary>
	/// Compresses a range of data.
	/// </summary>
	/// <param name="data">An array of uncompressed bytes.</param>
	public void Compress(byte[] data)
	{
		for (int i = 0; i < data.Length; i++)
		{
			C = FromByte(data[i]);
			string wc = W + C;
			if (!Codes.TryAdd(wc, NextCode))
			{
				W = wc;
				continue;
			}

			var entry = Codes[W];
			WriteCode(entry);
			NextCode++;
			W = C;
			UpdateRange(NextCode);
		}
	}

	/// <summary>
	/// Consumes LZW-compressed data according to a requested number of uncompressed output bytes.
	/// </summary>
	/// <param name="byteCount">The desired number of uncompressed bytes to return.</param>
	/// <returns>A range of uncompressed byte data.</returns>
	public byte[] Decompress(int byteCount = 1)
	{
		if (string.IsNullOrEmpty(W))
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

			if (!Packets.TryGetValue(k, out C))
				C = $"{W}{W[0]}";

			for (int i = 0; i < C.Length; i++)
				Incoming.Add((byte)C[i]);

			Packets.Add(NextCode++, $"{W}{C[0]}");
			W = C;
			UpdateRange(NextCode + 1);
		}

		var limit = Math.Min(byteCount, Incoming.Count);
		var range = Incoming[..limit];
		Incoming.RemoveRange(0, limit);
		return [.. range];
	}

	private static string FromByte(byte c) => $"{(char)c}";

	private static string FromChar(char c) => $"{c}";

	/// <summary>
	/// Initializes the LZW state engine.
	/// </summary>
	/// <param name="fileStream">An open filestream to or from which to read or write LZW-compressed data.</param>
	/// <param name="writing"><c>true</c> if we are compressing data and outputting it to the stream; <c>false</c> if we are reading and consuming LZW-compressed data.</param>
	public void Init(Stream? fileStream = null, bool writing = false)
	{
		FileStream = fileStream ?? FileStream;
		Open = true;
		Writing = writing;
		InitDictionary();
	}

	/// <summary>
	/// Initializes the LZW packet and code dictionaries to their default values.
	/// </summary>
	private void InitDictionary()
	{
		Codes.Clear();
		NextCode = 258U;
		Packets.Clear();
		Range = 9;

		for (char i = (char)0; i < 256; i++)
		{
			var ci = FromChar(i);
			Codes[ci] = i;
			Packets[i] = ci;
		}
	}

	/// <summary>
	/// Consumes <c>n</c> bits from the LZW stream (where <c>n</c> is the current LZW code range) and formats them as a single LZW code.
	/// </summary>
	/// <returns>The LZW code that was consumed from the stream.</returns>
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
				BitStream.Add((byte)(b >> 8 - i & 1));
		}

		for (int i = 1; i <= Range; i++)
			code += (uint)(BitStream[i - 1] << Range - i);
		BitStream.RemoveRange(0, Range);

		return code;
	}

	/// <summary>
	/// Checks for a need to dynamically expand the LZW code range, and expands it if needed.
	/// </summary>
	/// <param name="lastCode">The most recent code that was written to the LZW bit stream.</param>
	/// <exception cref="OverflowException">The current implementation does not reset the LZW dictionary past a certain size. If it reaches that size, LZW compression ceases to be more efficient than plaintext.</exception>
	private void UpdateRange(uint lastCode)
	{
		for (int i = Range; i <= MaxRange; i++)
		{
			if (lastCode < (uint)Math.Pow(2, i))
				break;
			Range = i + 1;
		}

		if (lastCode >= (uint)Math.Pow(2, MaxRange + 1))
			throw new OverflowException("Serialized database has exceeded the maximum capacity for restricted LZW compression.", new OverflowException());
	}

	/// <summary>
	/// Formats an LZW code according to the current range, and writes it to the bit stream.
	/// </summary>
	/// <param name="code">The code to be written.</param>
	private void WriteCode(uint code)
	{
		for (int i = 1; i <= Range; i++)
			BitStream.Add((byte)(code >> Range - i & 1));
	}
}
