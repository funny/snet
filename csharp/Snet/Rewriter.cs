using System;
using System.IO;

namespace Snet
{
	internal class Rewriter
	{
		private byte[] _Data;
		//private int _Begin;

		public Rewriter (int size)
		{
			_Data = new byte[size];
		}

		public void Push(byte[] b, int offset, int size) {
			if (size >= _Data.Length) {
				int drop = size - _Data.Length;

				Buffer.BlockCopy (b, offset + drop, _Data, 0, size - drop);
			} else {
				Buffer.BlockCopy (_Data, size, _Data, 0, _Data.Length - size);
				Buffer.BlockCopy (b, offset, _Data, _Data.Length - size, size);
			}
		}

		public bool Rewrite(Stream stream, ulong writeCount, ulong readCount) {
			int n = (int)writeCount - (int)readCount;

			if (n == 0) {
				return true;
			} else if (n < 0 || n > _Data.Length) {
				return false;
			}

			stream.Write(_Data, _Data.Length - n, n);
			return true;
		}
	}
}

