using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
	private byte[] _testBuffer = [];
	private bool _writing;

	/// <summary>
	/// See SIDB.md for a file format description.
	/// </summary>
	public required byte DatabaseFormat { get; set; }
	public bool Headless { get; private set; }
	public bool UseLZW { get; private set; }

	/// <summary>
	/// Clear previous buffers and prepare to perform a compression test.
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
		_testBuffer = [];
	}

	/// <summary>
	/// Flush the open filestream buffer and dispose it.
	/// </summary>
	public void Close(bool testing = false)
	{
		if (!_isOpen)
			return;

		_lzw.Close();
		_fileStream?.Flush();

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
	public byte[] GetOutgoingStream()
	{
		if (_writing)
			_lzw.Close();
		var _memoryStream = _fileStream as MemoryStream;
		return _memoryStream?.ToArray() ?? [];
	}

	/// <summary>
	/// Initialize the serializer, and optionally the LZW state engine, according to the features available with this database format.
	/// </summary>
	private void HandleFormat()
	{
		Headless = DatabaseFormat < 3;
		UseLZW = DatabaseFormat % 2 == 0;

		if (!UseLZW)
			return;

		_lzw.Init(DatabaseFormat, _fileStream, _writing);
	}

	/// <summary>
	/// Initialize the serializer's underlying stream for reading.
	/// </summary>
	/// <param name="path">The path to a file to encapsulate in the underlying <c>FileStream</c>. May be <c>null</c> if and only if <paramref name="inMemory"/> is not <c>null</c>.</param>
	/// <param name="inMemory">If not <c>null</c>, the engine will be initialized with an underlying <c>MemoryStream</c> with <paramref name="inMemory"/> as its buffer, instead of a <c>FileStream</c>.</param>
	/// <returns></returns>
	public bool OpenRead(string? path, in List<byte>? inMemory = null)
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
	/// <param name="path">The path to a file to encapsulate in the underlying <c>FileStream</c>. May be <c>null</c> if and only if <paramref name="inMemory"/> is <c>true</c>.</param>
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

			WriteHeader();
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

		int nextSize = DatabaseFormat > 12 ? (int)ReadUInt16() : (int)ReadUInt32();
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

	public string? ReadShortString()
	{
		if (!_isOpen)
			return null;

		if (DatabaseFormat < 13)
			return ReadString();

		var nextSize = ReadUInt16();
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

	private ushort ReadUInt16() => (ushort)ShortFromBytes(ReadBytes(2));

	private uint ReadUInt32() => (uint)IntFromBytes(ReadBytes(4));

	public void WriteByte(byte data)
	{
		if (UseLZW)
		{
			_lzw.Compress([data]);
			return;
		}
		_fileStream?.WriteByte(data);
	}

	private void WriteBytes(in byte[] data)
	{
		if (UseLZW)
		{
			_lzw.Compress(data);
			return;
		}
		_fileStream?.Write(data);
	}

	/// <summary>
	/// Write the database header to the stream.
	/// </summary>
	private void WriteHeader(byte? testFormat = null)
	{
		_fileStream?.Write(Encoding.UTF8.GetBytes(
			$"SYL {(char)(testFormat ?? DatabaseFormat)}"
		));

		HandleFormat();
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

		if (DatabaseFormat < 13)
			WriteUInt32((uint)_buffer.Length);
		else
			WriteUInt16((ushort)_buffer.Length);

		WriteBytes(_buffer);
	}

	public void WriteShortString(string? value)
	{
		if (!_isOpen)
			return;

		if (DatabaseFormat < 13)
		{
			WriteString(value);
			return;
		}

		_buffer = Encoding.UTF8.GetBytes(value ?? string.Empty);
		WriteUInt16((ushort)_buffer.Length);
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

	private void WriteUInt16(ushort data) => WriteBytes(ShortToBytes((short)data));

	private void WriteUInt32(uint data) => WriteBytes(IntToBytes((int)data));
}
