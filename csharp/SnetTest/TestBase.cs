using System;

namespace SnetTest
{
	public class TestBase
	{
		protected Random rand = new Random ();

		protected byte[] RandBytes(int n) {
			n = rand.Next (n) + 1;
			var b = new byte[n];
			for (var i = 0; i < n; i++) {
				b [i] = (byte)rand.Next (255);
			}
			return b;
		}

		protected bool BytesEquals(byte[] strA, byte[] strB) {
			int length = strA.Length;
			if (length != strB.Length){
				return false;
			}
			for (int i = 0; i < length; i++){
				if(strA[i] != strB[i] )
					return false;
			}
			return true;
		}
	}
}

