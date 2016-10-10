using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using Snet;

namespace SnetTest
{
	[TestFixture ()]
	public class RereaderTest : TestBase
	{
		[Test ()]
		public void Test_Rereader ()
		{
			var n = 1000000;
			var q = new Queue<byte[]> (n);
			var r = new Rereader ();

			for (var i = 0; i < n; i++) {
				var b = RandBytes (100);
				using (var ms = new MemoryStream (b)) {
					r.Reread (ms, b.Length);
				}
				q.Enqueue (b);
			}

			for (var i = 0; i < n; i++) {
				var raw = q.Dequeue ();
				var b = new byte[raw.Length];
				var offset = 0;
				var remind = raw.Length;
				while (remind > 0) {
					var size = rand.Next(remind + 1);
					if (size == 0) {
						continue;
					}
					r.Pull (b, offset, size);
					offset = offset + size;
					remind = remind - size;
				}
				Assert.True(BytesEquals (raw, b));
			}
		}
	}
}

