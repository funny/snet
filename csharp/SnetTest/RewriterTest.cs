using NUnit.Framework;
using System;
using System.IO;
using Snet;

namespace SnetTest
{
	[TestFixture ()]
	public class RewriterTest : TestBase
	{
		[Test ()]
		public void Test_Rewriter ()
		{
			ulong writeCount = 0;
			ulong readCount = 0;

			var w = new Rewriter (100);

			for (var i = 0; i < 1000000; i++) {
				var a = RandBytes (100);
				var b = new byte[a.Length];
				w.Push (a, 0, a.Length);
				writeCount += (ulong)a.Length;

				var remind = a.Length;
				var offset = 0;
				while (remind > 0) {
					var size = rand.Next (remind) + 1;

					using (MemoryStream ms = new MemoryStream(b, offset, b.Length - offset)) {
						Assert.True (w.Rewrite (ms, writeCount, readCount));
					}

					readCount += (ulong)size;
					offset += size;
					remind -= size;
				}

				Assert.True (BytesEquals (a, b));
			}
		}
	}
}

