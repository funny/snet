using System;

namespace Snet
{
	public class DH64
	{
		private const ulong p = 0xffffffffffffffc5;
		private const ulong g = 5;

		private static ulong mul_mod_p(ulong a, ulong b) {
			ulong m = 0;
			while (b > 0) {
				if ((b&1) > 0) {
					var t = p - a;
					if (m >= t) {
						m -= t;
					} else {
						m += a;
					}
				}
				if (a >= p-a) {
					a = a*2 - p;
				} else {
					a = a * 2;
				}
				b >>= 1;
			}
			return m;
		}

		private static ulong pow_mod_p(ulong a, ulong b) {
			if (b == 1) {
				return a;
			}
			var t = pow_mod_p(a, b>>1);
			t = mul_mod_p(t, t);
			if ((b%2) > 0) {
				t = mul_mod_p(t, a);
			}
			return t;
		}

		private static ulong powmodp(ulong a , ulong b) {
			if (a == 0) {
				throw new Exception("DH64 zero public key");
			}
			if (b == 0) {
				throw new Exception("DH64 zero private key");
			}
			if (a > p) {
				a %= p;
			}
			return pow_mod_p(a, b);
		}

		private Random rand;

		public DH64() {
			rand = new Random();
		}

		public void KeyPair(out ulong privateKey, out ulong publicKey) {
			var a = (ulong)rand.Next();
			var b = (ulong)rand.Next() + 1;
			privateKey = (a<<32) | b;
			publicKey = PublicKey(privateKey);
		}

		public ulong PublicKey(ulong privateKey) {
			return powmodp(g, privateKey);
		}

		public ulong Secret(ulong privateKey, ulong anotherPublicKey) {
			return powmodp(anotherPublicKey, privateKey);
		}
	}
}