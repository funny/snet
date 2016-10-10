using NUnit.Framework;
using System;
using Snet;

namespace SnetTest
{
	[TestFixture ()]
	public class SnetStreamTest : TestBase
	{
		private void StreamTest(bool enableCrypt, int port)
		{
			var stream = new SnetStream (1024, enableCrypt);

			stream.Connect ("127.0.0.1", port);

			for (int i = 0; i < 100000; i++) {
				var a = RandBytes (100);
				var b = a;
				var c = new byte[a.Length];

				if (enableCrypt) {
					b = new byte[a.Length];
					Buffer.BlockCopy (a, 0, b, 0, a.Length);
				}

				stream.Write (a, 0, a.Length);

				for (int n = c.Length; n > 0;) {
					n -= stream.Read (c, c.Length - n, n);
				}

				if (!BytesEquals (b, c))
					Assert.Fail ();
			}

			stream.Close ();
		}

		[Test()]
		public void Test_Stable_NoEncrypt()
		{
			StreamTest (false, 10010);
		}

		[Test()]
		public void Test_Stable_Encrypt()
		{
			StreamTest (true, 10011);
		}

		[Test()]
		public void Test_Unstable_NoEncrypt()
		{
			StreamTest (false, 10012);
		}

		[Test()]
		public void Test_Unstable_Encrypt()
		{
			StreamTest (true, 10013);
		}

		private void StreamTestAsync(bool enableCrypt, int port)
		{
			var stream = new SnetStream (1024, enableCrypt);

			var ar = stream.BeginConnect ("127.0.0.1", port, null, null);
			stream.WaitConnect (ar);

			for (int i = 0; i < 100000; i++) {
				var a = RandBytes (100);
				var b = a;
				var c = new byte[a.Length];

				if (enableCrypt) {
					b = new byte[a.Length];
					Buffer.BlockCopy (a, 0, b, 0, a.Length);
				}

				IAsyncResult ar1 = stream.BeginWrite(a, 0, a.Length, null, null);
				stream.EndWrite (ar1);

				IAsyncResult ar2 = stream.BeginRead(c, 0, c.Length, null, null);
				stream.EndRead(ar2);

				if (!BytesEquals (b, c))
					Assert.Fail ();
			}

			stream.Close ();
		}

		[Test()]
		public void Test_Stable_NoEncrypt_Async()
		{
			StreamTestAsync (false, 10010);
		}

		[Test()]
		public void Test_Stable_Encrypt_Async()
		{
			StreamTestAsync (true, 10011);
		}

		[Test()]
		public void Test_Unstable_NoEncrypt_Async()
		{
			StreamTestAsync (false, 10012);
		}

		[Test()]
		public void Test_Unstable_Encrypt_Async()
		{
			StreamTestAsync (true, 10013);
		}
	}
}

