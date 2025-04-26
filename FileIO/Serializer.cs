using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static SylverInk.Common;

namespace SylverInk.FileIO
{
	public partial class Serializer
	{
		private byte[] _buffer = [];
		private Stream? _fileStream;
		private bool _isOpen = false;
		private readonly LZW _lzw = new();
		private List<byte> _outgoing = [];
		private byte[] _testBuffer = [];
		private bool _writing = false;

		/// <summary>
		/// See SIDB.md for a file format description.
		/// </summary>
		public byte DatabaseFormat { get; set; } = (byte)HighestFormat;
		public bool Headless { get; private set; } = false;
		public bool UseLZW { get; private set; } = false;

		public void BeginCompressionTest()
		{
			Close();

			_fileStream = new MemoryStream();
			_isOpen = true;
			_writing = true;

			WriteHeader((byte)HighestFormat);
		}

		public void ClearCompressionTest()
		{
			Close();

			_fileStream = null;
			_outgoing = [];
			_testBuffer = [];
		}

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

		// WARNING: If writing, do not call until we're done.
		// It clears the LZW compression data.
		public List<byte> GetStream()
		{
			if (_writing)
			{
				_lzw.Close();
				_outgoing = _lzw.Outgoing;
			}
			var _memoryStream = _fileStream as MemoryStream;
			return _memoryStream?.ToArray().Concat(_outgoing).ToList() ?? [];
		}

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

		public bool OpenRead(string path, List<byte>? inMemory = null)
		{
			Close();

			try
			{
				_fileStream = inMemory is null ? new FileStream(path, FileMode.Open) : new MemoryStream(inMemory?.ToArray() ?? []);
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

		public bool OpenWrite(string path, bool inMemory = false)
		{
			Close();

			try
			{
				if (inMemory)
					_fileStream = new MemoryStream();
				else
				{
					if (File.Exists(path))
						File.Delete(path);

					_fileStream = new FileStream(path, FileMode.Create);
				}

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

		private byte[] ReadBytes(int byteCount)
		{
			if (UseLZW)
				return _lzw.Decompress(byteCount);

			_buffer = new byte[byteCount];
			_fileStream?.Read(_buffer, 0, byteCount);
			return _buffer;
		}

		private void ReadHeader()
		{
			_buffer = new byte[5];
			_fileStream?.Read(_buffer, 0, 5);

			string header = Encoding.UTF8.GetString(_buffer);
			DatabaseFormat = (byte)header[^1];

			HandleFormat();
		}

		public int ReadInt32(ref int item)
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
			catch
			{
				return item;
			}
		}

		public long ReadLong(ref long item)
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
			catch
			{
				return item;
			}
		}

		public string? ReadString(ref string? item)
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
			catch
			{
				return item;
			}
		}

		private uint ReadUInt32() => (uint)IntFromBytes(ReadBytes(4));

		private void WriteBytes(byte[] data)
		{
			if (UseLZW)
				_lzw.Compress(data);
			_outgoing.AddRange(data);
		}

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

			_buffer = Encoding.UTF8.GetBytes(value.ToString());
			WriteUInt32((uint)_buffer.Length);
			WriteBytes(_buffer);
		}

		public void WriteLong(long value)
		{
			if (!_isOpen)
				return;

			_buffer = Encoding.UTF8.GetBytes(value.ToString());
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
}
