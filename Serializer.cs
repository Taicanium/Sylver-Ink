using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SylverInk
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
		/// <para>
		/// Format 1: Uncompressed Headless.
		/// Data is serialized to plaintext.
		/// Four bytes are written indicating the object's length in string format, followed by that formatted string.
		/// </para>
		/// <para>
		/// Format 2: LZW Restricted Headless.
		/// Data is compressed using the Lempel–Ziv–Welch (LZW) algorithm.
		/// The LZW bit stream is formatted with a code dictionary at most 25 bits wide, yielding a length at most 2^25, or ~33.5 million.
		/// The dictionary is not reset when its width limit is reached. This results in exponential resource usage with increasing database size, eventually becoming prohibitive.
		/// </para>
		/// <para>
		/// Format 3: Uncompressed.
		/// Data is uncompressed, with the added feature of a database name/title at the start of data.
		/// </para>
		/// <para>
		/// Format 4: LZW Restricted.
		/// Data is compressed according to format 2, with the added feature of a database name/title at the start of data.
		/// </para>
		/// <para>
		/// Format 5: LZW Unrestricted.
		/// Not yet implemented.
		/// When implemented, this format will compress data with an LZW dictionary that resets once its width limit is reached, preventing runaway resource usage and allowing for much larger databases.
		/// </para>
		/// </summary>
		public byte DatabaseFormat { get; set; } = 4;
		public bool Headless { get; private set; } = false;
		public bool UseLZW { get; private set; } = false;

		public void BeginCompressionTest()
		{
			Close();

			_fileStream = new MemoryStream();
			_isOpen = true;
			_writing = true;

			WriteHeader(4);
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
			if (_isOpen)
			{
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
			}

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

		private void HandleFormat(int format = 0)
		{
			format = format < 1 ? DatabaseFormat : format;
			switch (format)
			{
				case 1:
					Headless = true;
					UseLZW = false;
					break;
				case 2:
					Headless = true;
					UseLZW = true;
					break;
				case 3:
					Headless = false;
					UseLZW = false;
					break;
				case 4:
					Headless = false;
					UseLZW = true;
					break;
			}

			if (UseLZW)
			{
				_lzw.Outgoing.Clear();
				_lzw.Init(_fileStream, _writing);
			}
		}

		public bool OpenRead(string path)
		{
			Close();

			try
			{
				_fileStream = new FileStream(path, FileMode.Open);
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

		public bool OpenWrite(string path)
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
			catch
			{
				return false;
			}

			return true;
		}

		private byte ReadByte()
		{
			if (UseLZW)
			{
				var b = _lzw.Decompress();
				if (b.Length > 0)
					return b[0];
				return 0;
			}

			return (byte)(_fileStream?.ReadByte() ?? 0);
		}

		private byte[] ReadBytes(int byteCount)
		{
			if (UseLZW)
			{
				var b = _lzw.Decompress(byteCount);
				return b;
			}

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

		private uint ReadUInt32()
		{
			return ReadByte() * 16777216U
			+ ReadByte() * 65536U
			+ ReadByte() * 256U
			+ ReadByte();
		}

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

		private void WriteUInt32(uint data)
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
