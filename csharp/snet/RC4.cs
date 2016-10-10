using System;
using System.IO;

namespace Snet
{
	public class RC4Cipher
	{
		private uint[] s = new uint[256];
		private byte i, j;

		public RC4Cipher(byte[] key) {
			int k = key.Length;
			if (k < 1 || k > 256) {
				throw new RC4KeySizeException(k);
			}

			for (uint i = 0; i < 256; i++) {
				s[i] = i;
			}

			byte j = 0;
			uint t = 0;
			for (int i = 0; i < 256; i++) {
				j = (byte)(j + s[i] + key[i % k]);
				t = s[i];
				s[i] = s[j];
				s[j] = t;
			}
		}

		public void XORKeyStream(byte[] dst, int dstOffset, byte[] src, int srcOffset, int count) {
			if (count == 0)
				return;

			byte i = this.i;
			byte j = this.j;
			uint t = 0;
			for (int k = 0; k < count; k ++) {
				i += 1;
				j = (byte)(s[i] + j);
				t = s[i];
				s[i] = s[j];
				s[j] = t;
				dst[k + dstOffset] = (byte)(src[k + srcOffset] ^ (byte)(s[(byte)(s[i] + s[j])]));
			}
			this.i = i;
			this.j = j;
		}
	}

	public class RC4Stream : Stream
	{
		private Stream stream;
		private RC4Cipher cipher;

		public RC4Stream(Stream stream, byte[] key) {
			this.stream = stream;
			this.cipher = new RC4Cipher(key);
		}

		public override int Read(byte[] buffer, int offset, int count) {
			count = stream.Read(buffer, offset, count);
			cipher.XORKeyStream(buffer, offset, buffer, offset, count);
			return count;
		}

		public override void Write(byte[] buffer, int offset, int count) {
			byte[] dst = new byte[count];
			cipher.XORKeyStream(dst, 0, buffer, offset, count);
			stream.Write(dst, 0, count);
		}

		public override bool CanRead {
			get { return stream.CanRead; }
		}

		public override bool CanSeek {
			get { return stream.CanSeek; }
		}

		public override bool CanWrite {
			get { return stream.CanWrite; }
		}

		public override long Length {
			get { return stream.Length; }
		}

		public override long Position {
			get { return stream.Position; }
			set { stream.Position = value; }
		}

		public override long Seek(long offset, SeekOrigin origin) {
			return stream.Seek(offset, origin);
		}

		public override void SetLength(long length) {
			stream.SetLength(length);
		}

		public override void Flush() {
			stream.Flush();
		}
	}

	public class RC4KeySizeException : Exception
	{
		private int size;

		public RC4KeySizeException(int size) {
			this.size = size;
		}

		public override string Message {
			get { return "RC4Stream: invalid key size " + size; }
		}
	}
}