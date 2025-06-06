using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using static SylverInk.CommonUtils;
using static SylverInk.FileIO.FileUtils;

namespace SylverInk.FileIO;

public partial class Serializer : IDisposable
{
	private byte[] _buffer = [];
	private Stream? _fileStream;
	private bool _isOpen;
	private readonly LZW _lzw = new();
	private List<byte> _outgoing = [];
	private byte[] _testBuffer = [];
	private bool _writing;

	/// <summary>
	/// See SIDB.md for a file format description.
	/// </summary>
	public byte DatabaseFormat { get; set; } = (byte)HighestSIDBFormat;
	public bool Headless { get; private set; }
	public bool UseLZW { get; private set; }

	/// <summary>
	/// Clear previous buffers and prepare to perform a compressun test.
	/// </summary>
	public void BeginCompressionTest()
	{
		Close();

		_fileStream = new MemoryStream();
		_isOpen = true;
		_writing = true;

		WriteHeader((byte)HighestSIDBFormat);
	}

	/// <summary>
	/// Clear all current buffers after a compression test.
	/// </summary>
	public void ClearCompressionTest()
	{
		Close();

		_fileStream = null;
		_outgoing.Clear();
		_testBuffer = [];
	}

	/// <summary>
	/// Flush the open filestream buffer and dispose it.
	/// </summary>
	/// <param name="testing"><c>true</c> if we are performing a compression test.</param>
	public void Close(bool testing = false)
	{
		if (!_isOpen)
			return;

		_lzw.Close();
		if (_writing)
		{
			if (UseLZW)
				_outgoing = _lzw.Outgoing;
			_fileStream?.Write([.. _outgoing], 0, _outgoing.Count);
			_fileStream?.Flush();
		}

		if (!testing)
			_fileStream?.Dispose();

		_isOpen = false;
	}

	public void Dispose()
	{
		_fileStream?.Dispose();
		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// Flush the compression test buffers, then read back from them to confirm the test's success.
	/// </summary>
	public void EndCompressionTest()
	{
		Close(true);

		var _memoryStream = _fileStream as MemoryStream;
		_testBuffer = _memoryStream?.ToArray() ?? [];
		_fileStream = new MemoryStream(_testBuffer, false);
		_isOpen = true;
		_writing = false;

		ReadHeader();
	}

	/// <summary>
	/// WARNING: This method consumes the LZW state buffers. Do not call this method until we are ready to close the stream.
	/// </summary>
	/// <returns>The outgoing compression buffer</returns>
	public List<byte> GetOutgoingStream()
	{
		if (_writing)
		{
			_lzw.Close();
			_outgoing = _lzw.Outgoing;
		}
		var _memoryStream = _fileStream as MemoryStream;
		return _memoryStream?.ToArray().Concat(_outgoing).ToList() ?? [];
	}

	/// <summary>
	/// Initialize the serializer, and optionally the LZW state engine, according to the features available with this database format.
	/// </summary>
	/// <param name="format">The format of the database</param>
	private void HandleFormat(int format = 0)
	{
		format = format < 1 ? DatabaseFormat : format;
		Headless = format < 3;
		UseLZW = format % 2 == 0;

		if (!UseLZW)
			return;

		_lzw.Outgoing.Clear();
		_lzw.Init(_fileStream, _writing);
	}

	/// <summary>
	/// Initialize the serializer's underlying stream for reading.
	/// </summary>
	/// <param name="path">The path to a file to encapsulate in the underlying <c>FileStream</c>. May be <c>null</c> if <paramref name="inMemory"/> is not <c>null</c>.</param>
	/// <param name="inMemory">If not <c>null</c>, the engine will be initialized with an underlying <c>MemoryStream</c> with <paramref name="inMemory"/> as its buffer, instead of a <c>FileStream</c>.</param>
	/// <returns></returns>
	public bool OpenRead(string? path, List<byte>? inMemory = null)
	{
		Close();

		try
		{
			_fileStream = inMemory is null ? new FileStream(path ?? string.Empty, FileMode.Open) : new MemoryStream(inMemory?.ToArray() ?? []);
			_isOpen = true;
			_writing = false;

			ReadHeader();
		}
		catch
		{
			return false;
		}

		return true;
	}

	/// <summary>
	/// Initialize the serializer's underlying stream for writing.
	/// </summary>
	/// <param name="path">The path to a file to encapsulate in the underlying <c>FileStream</c>. May be <c>null</c> if <paramref name="inMemory"/> is <c>true</c>.</param>
	/// <param name="inMemory">If <c>true</c>, the engine will be initialized with an underlying <c>MemoryStream</c>, instead of a <c>FileStream</c>.</param>
	/// <returns></returns>
	public bool OpenWrite(string? path, bool inMemory = false)
	{
		Close();

		try
		{
			_fileStream = inMemory ? new MemoryStream() : new FileStream(path ?? string.Empty, FileMode.Create);
			_isOpen = true;
			_writing = true;

			WriteHeader(DatabaseFormat);
		}
		catch
		{
			return false;
		}

		return true;
	}

	/// <summary>
	/// Consume a single uncompressed byte from the stream, and decompress as needed.
	/// </summary>
	public byte ReadByte()
	{
		if (UseLZW)
			return _lzw.Decompress()[0];

		_buffer = new byte[1];
		_fileStream?.Read(_buffer, 0, 1);
		return _buffer[0];
	}

	/// <summary>
	/// Consume a certain number of uncompressed bytes from the stream, and decompress as needed.
	/// </summary>
	/// <param name="byteCount"></param>
	private byte[] ReadBytes(int byteCount)
	{
		if (UseLZW)
			return _lzw.Decompress(byteCount);

		_buffer = new byte[byteCount];
		_fileStream?.Read(_buffer, 0, byteCount);
		return _buffer;
	}

	/// <summary>
	/// Parse the database header.
	/// </summary>
	private void ReadHeader()
	{
		_buffer = new byte[5];
		_fileStream?.Read(_buffer, 0, 5);

		string header = Encoding.UTF8.GetString(_buffer);
		DatabaseFormat = (byte)header[^1];

		HandleFormat();
	}

	public int? ReadInt32()
	{
		if (!_isOpen)
			return null;

		int nextSize = (int)ReadUInt32();
		if (nextSize == 0)
			return null;

		try
		{
			_buffer = ReadBytes(nextSize);
			if (int.TryParse(Encoding.UTF8.GetString(_buffer), out int item))
				return item;

			return null;
		}
		catch
		{
			return null;
		}
	}

	public long? ReadLong()
	{
		if (!_isOpen)
			return null;

		int nextSize = (int)ReadUInt32();
		if (nextSize == 0)
			return null;

		try
		{
			_buffer = ReadBytes(nextSize);
			if (long.TryParse(Encoding.UTF8.GetString(_buffer), out long item))
				return item;

			return null;
		}
		catch
		{
			return null;
		}
	}

	public string? ReadString()
	{
		if (!_isOpen)
			return null;

		int nextSize = (int)ReadUInt32();
		if (nextSize == 0)
			return null;

		try
		{
			_buffer = ReadBytes(nextSize);
			return Encoding.UTF8.GetString(_buffer);
		}
		catch
		{
			return null;
		}
	}

	private uint ReadUInt32() => (uint)IntFromBytes(ReadBytes(4));

	public void WriteByte(byte data)
	{
		if (UseLZW)
			_lzw.Compress([data]);
		_outgoing.AddRange([data]);
	}

	private void WriteBytes(byte[] data)
	{
		if (UseLZW)
			_lzw.Compress(data);
		_outgoing.AddRange(data);
	}

	/// <summary>
	/// Write the database header to the stream.
	/// </summary>
	/// <param name="format">The SIDB version number</param>
	private void WriteHeader(byte format)
	{
		_fileStream?.Write(Encoding.UTF8.GetBytes(
			$"SYL {(char)format}"
		));

		HandleFormat(format);
	}

	public void WriteInt32(int value)
	{
		if (!_isOpen)
			return;

		_buffer = Encoding.UTF8.GetBytes(value.ToString(NumberFormatInfo.InvariantInfo));
		WriteUInt32((uint)_buffer.Length);
		WriteBytes(_buffer);
	}

	public void WriteLong(long value)
	{
		if (!_isOpen)
			return;

		_buffer = Encoding.UTF8.GetBytes(value.ToString(NumberFormatInfo.InvariantInfo));
		WriteUInt32((uint)_buffer.Length);
		WriteBytes(_buffer);
	}

	public void WriteString(string? value)
	{
		if (!_isOpen)
			return;

		_buffer = Encoding.UTF8.GetBytes(value ?? string.Empty);
		WriteUInt32((uint)_buffer.Length);
		WriteBytes(_buffer);
	}

	private void WriteUInt32(uint data) => WriteBytes(IntToBytes((int)data));
}
