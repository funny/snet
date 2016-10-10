using System;
using System.IO;

namespace Snet
{
	internal class Rewriter
	{
		private byte[] _Data;
		private int _Begin;

		public Rewriter (int size)
		{
			_Data = new byte[size];
		}

		public void Push(byte[] b, int offset, int size) {
			while (size > 0) {
				int n = _Data.Length - _Begin;
				if (n > b.Length - offset) {
					n = b.Length - offset;
				}
				Buffer.BlockCopy (b, offset, _Data, _Begin, n);
				_Begin = (_Begin + n) % _Data.Length;
				offset += n;
				size -= n;
			}
		}

		public bool Rewrite(Stream stream, ulong writeCount, ulong readCount) {
			int n = (int)writeCount - (int)readCount;

			if (n == 0) {
				return true;
			} else if (n < 0 || n > _Data.Length) {
				return false;
			} else if (writeCount <= (ulong)_Data.Length) {
				try {
					stream.Write(_Data, (int)readCount, n);
					return true;
				} catch {
					return false;
				}
			}

			int begin = (_Begin + (_Data.Length - n)) % _Data.Length;
			int end = begin + n;
			if (end > _Data.Length) {
				end = _Data.Length;
			}

			try {
				stream.Write(_Data, begin, end - begin);

				n -= end - begin;
				if (n != 0){
					stream.Write(_Data, 0, n);
				}
				return true;
			} catch {
				return false;
			}
		}
	}
}

