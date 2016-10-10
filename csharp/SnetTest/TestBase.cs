using System;

namespace SnetTest
{
	public class TestBase
	{
		protected Random rand = new Random ();

		protected byte[] RandBytes(int n) {
			var b = new byte[n];
			rand.NextBytes (b);
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

