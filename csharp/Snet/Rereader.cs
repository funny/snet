using System;
using System.IO;

namespace Snet
{
	internal class RereadData {
		public byte[] Data;
		public int Offset;
		public RereadData Next;
	}

	internal class Rereader {
		private RereadData _Head;
		private RereadData _Tail;
		private ulong _Count;

		public ulong Count {
			get { return _Count; }
		}

		public int Pull(byte[] buffer, int offset, int size) {
			if (_Head != null) {
				int headRemind = _Head.Data.Length - _Head.Offset;
				int count = headRemind < size ? headRemind : size;
				Buffer.BlockCopy (_Head.Data, _Head.Offset, buffer, offset, count);
				_Head.Offset += count;
				if (_Head.Offset >= _Head.Data.Length) {
					_Head = _Head.Next;
					if (_Head == null) {
						_Tail = null;
					}
				}
				_Count -= (ulong)count;
				return count;
			}
			return 0;
		}

		public bool Reread(Stream stream, int n) {
			byte[] b = new byte[n];
			try {
				for (int x = n; x >0; ) {
					x -= stream.Read(b, n - x, x);
					if (x == n)
						return false;
				}
			} catch {
				return false;
			}
			RereadData data = new RereadData ();
			data.Data = b;
			if (_Head == null) {
				_Head = data;
			} else {
				_Tail.Next = data;
			}
			_Tail = data;
			_Count += (ulong)n;
			return true;
		}
	}
}

