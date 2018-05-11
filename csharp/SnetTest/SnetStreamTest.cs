using NUnit.Framework;
using System;
using Snet;

namespace SnetTest
{
	[TestFixture ()]
	public class SnetStreamTest : TestBase
	{
		private void StreamTest(bool enableCrypt, bool reconn, int port)
		{
			var stream = new SnetStream (1024, enableCrypt);

			stream.Connect ("127.0.0.1", port);

			for (int i = 0; i < 1000; i++) {
				var a = RandBytes (100);
				var b = a;
				var c = new byte[a.Length];

				if (enableCrypt) {
					b = new byte[a.Length];
					Buffer.BlockCopy (a, 0, b, 0, a.Length);
				}

				stream.Write (a, 0, a.Length);

				if (reconn && i % 100 == 0) {
					if (!stream.TryReconn ())
						Assert.Fail ();
				}

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
			StreamTest (false, false, 10010);
		}

		[Test()]
		public void Test_Stable_Encrypt()
		{
			StreamTest (true, false, 10011);
		}

		[Test()]
		public void Test_Unstable_NoEncrypt()
		{
			StreamTest (false, false, 10012);
		}

		[Test()]
		public void Test_Unstable_Encrypt()
		{
			StreamTest (true, false, 10013);
		}

		[Test()]
		public void Test_Stable_NoEncrypt_Reconn()
		{
			StreamTest (false, true, 10010);
		}

		[Test()]
		public void Test_Stable_Encrypt_Reconn()
		{
			StreamTest (true, true, 10011);
		}

		[Test()]
		public void Test_Unstable_NoEncrypt_Reconn()
		{
			StreamTest (false, true, 10012);
		}

		[Test()]
		public void Test_Unstable_Encrypt_Reconn()
		{
			StreamTest (true, true, 10013);
		}

		private void StreamTestAsync(bool enableCrypt, bool reconn, int port)
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

				if (reconn && i % 100 == 0) {
					if (!stream.TryReconn ())
						Assert.Fail ();
				}

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
			StreamTestAsync (false, false, 10010);
		}

		[Test()]
		public void Test_Stable_Encrypt_Async()
		{
			StreamTestAsync (true, false, 10011);
		}

		[Test()]
		public void Test_Unstable_NoEncrypt_Async()
		{
			StreamTestAsync (false, false, 10012);
		}

		[Test()]
		public void Test_Unstable_Encrypt_Async()
		{
			StreamTestAsync (true, false, 10013);
		}

		[Test()]
		public void Test_Stable_NoEncrypt_Async_Reconn()
		{
			StreamTestAsync (false, true, 10010);
		}

		[Test()]
		public void Test_Stable_Encrypt_Async_Reconn()
		{
			StreamTestAsync (true, true, 10011);
		}

		[Test()]
		public void Test_Unstable_NoEncrypt_Async_Reconn()
		{
			StreamTestAsync (false, true, 10012);
		}

		[Test()]
		public void Test_Unstable_Encrypt_Async_Reconn()
		{
			StreamTestAsync (true, true, 10013);
		}

		[Test()]
		public void Test_BadServer()
		{
			var stream = new SnetStream (1024, false);

			stream.ReadTimeout = 3000;
			stream.WriteTimeout = 3000;
			stream.ConnectTimeout = 3000;

			string err = null;

			var wait = new System.Threading.ManualResetEvent (false);

			stream.BeginConnect ("127.0.0.1", 10014, (IAsyncResult ar) => {
				try {
					stream.EndConnect(ar);
				} catch (Exception ex) {
					err = ex.ToString ();
				}
				wait.Set();
			}, null);

			wait.WaitOne (new TimeSpan (0, 0, 4));

			Assert.IsNotNull(err);

			Console.WriteLine (err);
		}

		[Test()]
		public void Test_ConnectTimeout()
		{
			var stream = new SnetStream (1024, false);

			stream.ReadTimeout = 3000;
			stream.WriteTimeout = 3000;
			stream.ConnectTimeout = 3000;

			string err = null;

			var wait = new System.Threading.ManualResetEvent (false);

			stream.BeginConnect ("192.168.2.20", 10000, (IAsyncResult ar) => {
				try {
					stream.EndConnect(ar);
				} catch (Exception ex) {
					err = ex.ToString ();
				}
				wait.Set();
			}, null);

			wait.WaitOne (new TimeSpan (0, 0, 4));

			Assert.IsNotNull(err);

			Console.WriteLine (err);
		}
	}
}

