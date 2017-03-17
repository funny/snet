using System;
using System.IO;

namespace Snet
{
	internal class Rewriter
	{
		private byte[] _Data;
		private int _head;

		private int _len;

		public Rewriter (int size)
		{
			_Data = new byte[size];
		}

		public void Push(byte[] b, int offset, int size) {
			if (size >= _Data.Length) {
				int drop = size - _Data.Length;

				Buffer.BlockCopy (b, offset + drop, _Data, 0, size - drop);
				_head = 0;
				if (_len != _Data.Length){
					_len = _Data.Length;
				}
			} else {
				int space = _Data.Length - _head;
				if (space >= size){
					Buffer.BlockCopy(b, offset, _Data, _head, size);
					if (space == size){
						_head = 0;
					} else{
						_head += size;
					}

					if (_len != _Data.Length){
						_len = Math.Min(_len + size, _Data.Length);
					}
				} else{
					Buffer.BlockCopy(b, offset, _Data, _head, space);
					Buffer.BlockCopy(b, offset + space, _Data, 0, size - space);
					_head = size - space;

					if (_len != _Data.Length){
						_len = _Data.Length;
					}
				}
			}
		}

		public bool Rewrite(Stream stream, ulong writeCount, ulong readCount) {
			int n = (int)writeCount - (int)readCount;

			if (n == 0) {
				return true;
			} else if (n < 0 || n > _len) {
				return false;
			}

			int offset = _head - n;
			if (offset >= 0){
				stream.Write(_Data, offset, n);
			} else{
				offset += _Data.Length;
				stream.Write(_Data, offset, _Data.Length - offset);
				stream.Write(_Data, 0, _head);
			}
			return true;
		}
	}
}

