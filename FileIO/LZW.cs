﻿using System;
using System.Collections.Generic;
using System.IO;

namespace SylverInk.FileIO;

public partial class LZW
{
	private readonly List<byte> BitStream = [];
	private readonly Dictionary<string, uint> Codes = [];
	private Stream? FileStream;
	private readonly List<byte> Incoming = [];
	private uint LastCode;
	private readonly int MaxRestrictedRange = 25;
	private readonly int MaxRange = 13;
	private uint NextCode = 258U;
	private bool Open;
	private readonly Dictionary<uint, string> Packets = [];
	private int Range = 9;
	private string W = string.Empty;
	private bool Writing;

	public int Format { get; private set; }
	public List<byte> Outgoing { get; } = [];

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
			string entry = FromByte(data[i]);
			string wc = W + entry;
			if (!Codes.TryAdd(wc, NextCode))
			{
				W = wc;
				continue;
			}

			WriteCode(Codes[W]);
			NextCode++;
			W = entry;

			if (Format >= 11 && NextCode > Math.Pow(2, MaxRange) - 2)
			{
				WriteCode(256U);
				InitDictionary();
				continue;
			}

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
			ReadFirstCode();

		if (LastCode == 257U)
			return [];

		while (Incoming.Count == 0 || Incoming.Count < byteCount)
		{
			LastCode = ReadCode();
			if (LastCode == 257U)
				break;

			if (Format >= 11 && LastCode == 256U)
			{
				InitDictionary();
				ReadFirstCode();
				continue;
			}

			if (!Packets.TryGetValue(LastCode, out string? entry))
				entry = $"{W}{W[0]}";

			for (int i = 0; i < entry.Length; i++)
				Incoming.Add((byte)entry[i]);

			Packets.Add(NextCode++, $"{W}{entry[0]}");
			W = entry;
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
	/// <param name="writing"><c>true</c> if we are compressing data and outputting it to the stream; <c>false</c> if we are reading and consuming LZW-compressed data.</param>
	public void Init(int format, Stream? fileStream = null, bool writing = false)
	{
		FileStream = fileStream ?? FileStream;
		Format = format;
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

	private void ReadFirstCode()
	{
		LastCode = ReadCode();
		if (LastCode == 257U)
			return;

		W = Packets[LastCode];
		for (int i = 0; i < W.Length; i++)
			Incoming.Add((byte)W[i]);
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

		if (lastCode >= (uint)Math.Pow(2, MaxRestrictedRange))
			throw new OverflowException("Serialized database has exceeded the maximum capacity for restricted LZW compression.", new OverflowException());
	}

	/// <summary>
	/// Formats an LZW code according to the current range, and writes it to the bit stream.
	/// </summary>
	private void WriteCode(uint code)
	{
		for (int i = 1; i <= Range; i++)
			BitStream.Add((byte)(code >> Range - i & 1));
	}
}
