﻿using System;
using System.IO;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Snet
{
	public class SnetStream : Stream
	{
        private const byte TypeNewconn = 0x00;
        private const byte TypeReconn = 0xFF;
        private ulong     _ID;
		private IPAddress  _Host;
		private int       _Port;
		private byte[]    _Key = new byte[8];
		private bool      _EnableCrypt;
		private RC4Cipher _ReadCipher;
		private RC4Cipher _WriteCipher;

		private Mutex            _ReadLock = new Mutex ();
		private Mutex            _WriteLock = new Mutex ();
		private ReaderWriterLock _ReconnLock = new ReaderWriterLock();

		private NetworkStream _BaseStream;
		private Rewriter      _Rewriter;
		private Rereader      _Rereader;

		private ulong _ReadCount;
		private ulong _WriterCount;

		private bool _Closed;

		public SnetStream (int size, bool enableCrypt)
		{
			_EnableCrypt = enableCrypt;
			_Rewriter = new Rewriter (size);
			_Rereader = new Rereader ();

			ConnectTimeout = 10000;
		}

		public override bool CanRead {
			get { return true; }
		}

		public override bool CanSeek {
			get { return false; }
		}

		public override bool CanWrite {
			get { return true; }
		}

		public override long Length {
			get { throw new NotSupportedException (); }
		}

		public override long Position {
			get { throw new NotSupportedException (); }
			set { throw new NotSupportedException (); }
		}

		public override void SetLength (long value)
		{
			throw new NotImplementedException ();
		}

		public override long Seek (long offset, SeekOrigin origin)
		{
			throw new NotImplementedException ();
		}

		internal class AsyncResult : IAsyncResult 
		{
			internal AsyncResult(AsyncCallback callback, object state) {
				this.Callback = callback;
				this.AsyncState = state;
				this.IsCompleted = false;
				this.AsyncWaitHandle = new ManualResetEvent(false);
			}
			internal AsyncCallback Callback {
				get;
				set;
			}
			public object AsyncState {
				get;
				internal set;
			}
			public WaitHandle AsyncWaitHandle {
				get;
				internal set;
			}
			public bool CompletedSynchronously {
				get { return false; }
			}
			public bool IsCompleted {
				get;
				internal set;
			}
			internal int ReadCount {
				get;
				set;
			}
			internal Exception Error {
				get;
				set;
			}
			internal int Wait() {
				AsyncWaitHandle.WaitOne ();
				if (Error != null)
					throw Error;
				return ReadCount;
			}
		}

		public IAsyncResult BeginConnect(string host, int port, AsyncCallback callback, object state)
		{
			if (_BaseStream != null)
				throw new InvalidOperationException ();

			AsyncResult ar1 = new AsyncResult (callback, state);
			ThreadPool.QueueUserWorkItem ((object ar2) => {
				AsyncResult ar3 = (AsyncResult)ar2;
				try {
					Connect(host, port);
				} catch (Exception ex) {
					ar3.Error = ex;
				}
				ar3.IsCompleted = true;
				((ManualResetEvent)ar3.AsyncWaitHandle).Set();
				if (ar3.Callback != null)
					ar3.Callback(ar3);
			}, ar1);

			return ar1;
		}

		public void WaitConnect(IAsyncResult asyncResult)
		{
			((AsyncResult)asyncResult).Wait ();
		}

		public void EndConnect(IAsyncResult asyncResult)
		{
			((AsyncResult)asyncResult).Wait ();
		}

		public void Connect(string host, int port)
		{
			if (_BaseStream != null)
				throw new InvalidOperationException ();

			_Host = Dns.GetHostAddresses (host)[0];
			_Port = port;
			handshake ();
		}

		private void handshake()
		{
            byte[] preRequest = new byte[1];
            byte[] request = new byte[24];
            byte[] response = request;

            preRequest[0] = TypeNewconn;

            ulong privateKey;
			ulong publicKey;
			DH64 dh64 = new DH64 ();
			dh64.KeyPair (out privateKey, out publicKey);

			using (MemoryStream ms = new MemoryStream (request, 0, 8)) {
				using (BinaryWriter w = new BinaryWriter (ms)) {
					w.Write (publicKey);
				}
			}

            TcpClient client = new TcpClient (_Host.AddressFamily);
            var ar = client.BeginConnect(_Host, _Port, null, null);
            ar.AsyncWaitHandle.WaitOne(new TimeSpan(0, 0, 0, 0, ConnectTimeout));
            if (!ar.IsCompleted)
            {
                throw new TimeoutException();
            }
            client.EndConnect(ar);

			setBaseStream (client.GetStream ());
			_BaseStream.Write (preRequest, 0, preRequest.Length);
			_BaseStream.Write (request, 0, 8);

			for (int n = 24; n > 0;) {
				int x = _BaseStream.Read (response, 24 - n, n);
				if (x == 0)
					throw new EndOfStreamException ();
				n -= x;
			}

            ulong challengeCode = 0;
            using (MemoryStream ms = new MemoryStream(response, 0, 24))
            {
                using (BinaryReader r = new BinaryReader(ms))
                {
                    ulong serverPublicKey = r.ReadUInt64();
                    ulong secret = dh64.Secret(privateKey, serverPublicKey);

                    using (MemoryStream ms2 = new MemoryStream(_Key))
                    {
                        using (BinaryWriter w = new BinaryWriter(ms2))
                        {
                            w.Write(secret);
                        }
                    }

                    _ReadCipher = new RC4Cipher(_Key);
                    _WriteCipher = new RC4Cipher(_Key);
                    _ReadCipher.XORKeyStream(response, 8, response, 8, 8);

                    _ID = r.ReadUInt64();

                    using (MemoryStream ms2 = new MemoryStream(request, 0, 16))
                    {
                        using (BinaryWriter w = new BinaryWriter(ms2))
                        {
                            w.Write(response, 16, 8);
                            w.Write(_Key);
                            MD5 md5 = MD5CryptoServiceProvider.Create();
                            byte[] hash = md5.ComputeHash(request, 0, 16);
                            Buffer.BlockCopy(hash, 0, request, 0, hash.Length);
                            _BaseStream.Write(request, 0, 16);
                        }
                    }
                }
            }
        }

		public override IAsyncResult BeginRead (byte[] buffer, int offset, int count, AsyncCallback callback, object state)
		{
			AsyncResult ar1 = new AsyncResult (callback, state);
			ThreadPool.QueueUserWorkItem ((object ar2) => {
				AsyncResult ar3 = (AsyncResult)ar2;
				try {
					while (ar3.ReadCount != count) {
						ar3.ReadCount += Read(buffer, offset + ar3.ReadCount, count - ar3.ReadCount);
					}
				} catch(Exception ex) {
					ar3.Error = ex;
				}
				ar3.IsCompleted = true;
				((ManualResetEvent)ar3.AsyncWaitHandle).Set();
				if (ar3.Callback != null)
					ar3.Callback(ar3);
			}, ar1);
			return ar1;
		}

		public override int EndRead (IAsyncResult asyncResult)
		{
			return ((AsyncResult)asyncResult).Wait ();
		}

		public override IAsyncResult BeginWrite (byte[] buffer, int offset, int count, AsyncCallback callback, object state)
		{
			AsyncResult ar1 = new AsyncResult (callback, state);
			ThreadPool.QueueUserWorkItem ((object ar2) => {
				AsyncResult ar3 = (AsyncResult)ar2;
				try {
					Write(buffer, offset, count);
				} catch(Exception ex) {
					ar3.Error = ex;
				}
				ar3.IsCompleted = true;
				((ManualResetEvent)ar3.AsyncWaitHandle).Set();
				if (ar3.Callback != null)
					ar3.Callback(ar3);
			}, ar1);
			return ar1;
		}

		public override void EndWrite (IAsyncResult asyncResult)
		{
			((AsyncResult)asyncResult).Wait ();
		}

		public override int Read (byte[] buffer, int offset, int size)
		{
			_ReadLock.WaitOne ();
			_ReconnLock.AcquireReaderLock (-1);
			int n = 0;
			try {
				for(;;) {
					n = _Rereader.Pull (buffer, offset, size);
					if (n > 0) {
						return n;
					}

					try {
						n = _BaseStream.Read (buffer, offset + n, size);
						if (n == 0) {
							if (!tryReconn())
								throw new IOException();
							continue;
						}
					} catch {
						if (!tryReconn())
							throw;
						continue;
					}
					break;
				}
			} finally {
				if (n > 0 && _EnableCrypt) {
					_ReadCipher.XORKeyStream (buffer, offset, buffer, offset, n);
				}
				_ReadCount += (ulong)n;
				_ReconnLock.ReleaseReaderLock ();
				_ReadLock.ReleaseMutex ();
			}
			return n;
		}

		public override void Write (byte[] buffer, int offset, int size)
		{
			if (size == 0)
				return;

			_WriteLock.WaitOne ();
			_ReconnLock.AcquireReaderLock (-1);
			try {
				if (_EnableCrypt) {
					_WriteCipher.XORKeyStream(buffer, offset, buffer, offset, size);
				}
				_Rewriter.Push(buffer, offset, size);
				_WriterCount += (ulong)size;

				try {
					_BaseStream.Write(buffer, offset, size);
				} catch {
					if (!tryReconn())
						throw;
				}
			} finally {
				_ReconnLock.ReleaseReaderLock ();
				_WriteLock.ReleaseMutex ();
			}
		}

		public bool TryReconn()
		{
			_ReconnLock.AcquireReaderLock (-1);
			bool result = tryReconn();
			_ReconnLock.ReleaseReaderLock ();
			return result;
		}

		private bool tryReconn()
		{
			_BaseStream.Close ();
			NetworkStream badStream = _BaseStream;

			_ReconnLock.ReleaseReaderLock ();
			_ReconnLock.AcquireWriterLock (-1);

			try {
				if (badStream != _BaseStream)
					return true;
                byte[] preRequest = new byte[1];
				byte[] request = new byte[24 + 16];
				byte[] response = new byte[24];
                preRequest[0] = TypeReconn;
				using (MemoryStream ms = new MemoryStream(request)) {
					using (BinaryWriter w = new BinaryWriter(ms)) {
						w.Write(_ID);
						w.Write(_WriterCount);
						w.Write(_ReadCount + _Rereader.Count);
						w.Write(_Key);
					}
				}

				MD5 md5 = MD5CryptoServiceProvider.Create();
				byte[] hash = md5.ComputeHash(request, 0, 32);
				Buffer.BlockCopy(hash, 0, request, 24, hash.Length);

				for (int i = 0; !_Closed; i ++) {
					if (i > 0)
						Thread.Sleep(3000);

					try {
						TcpClient client = new TcpClient(_Host.AddressFamily);

						var ar = client.BeginConnect(_Host, _Port, null, null);
						ar.AsyncWaitHandle.WaitOne(new TimeSpan(0, 0, 0, 0, ConnectTimeout));
						if (!ar.IsCompleted) {
							throw new TimeoutException ();
						}
						client.EndConnect(ar);

						NetworkStream stream = client.GetStream();
                        stream.Write(preRequest,0,preRequest.Length);
						stream.Write(request, 0, request.Length);

						for (int n = response.Length; n > 0;) {
							int x = stream.Read(response, response.Length - n, n);
							if (x == 0)
								throw new EndOfStreamException();
							n -= x;
						}

						ulong writeCount = 0;
						ulong readCount = 0;
                        ulong challengeCode = 0;
						using (MemoryStream ms = new MemoryStream(response)) {
							using (BinaryReader r = new BinaryReader(ms)) {
								writeCount = r.ReadUInt64();
								readCount = r.ReadUInt64();
                                challengeCode = r.ReadUInt64();
                                if (writeCount == 0 && readCount == 0 && challengeCode == 0) {
                                    stream.Close();
                                    return false;
                                }
							}
						}

                        using (MemoryStream ms = new MemoryStream(request, 0, 16)) {
                            using (BinaryWriter w = new BinaryWriter(ms)) {
                                w.Write(response, 16, 8);
                                w.Write(_Key);
                            }
                        }
                        hash = md5.ComputeHash(request, 0, 16);
                        Buffer.BlockCopy(hash, 0, request, 0, hash.Length);
                        stream.Write(request, 0, 16);

                        if (writeCount < _ReadCount)
                            return false;

                        if (_WriterCount < readCount)
                            return false;

						if (doReconn(stream, writeCount, readCount))
							return true;
					} catch {
						continue;
					}
				}
			} finally {
				_ReconnLock.ReleaseWriterLock ();
				_ReconnLock.AcquireReaderLock (-1);
			}
			return false;
		}

		private bool doReconn(NetworkStream stream, ulong writeCount, ulong readCount)
		{
			Thread thread = null;
			bool rereadSucceed = false;

			if (writeCount != _ReadCount) {
				thread = new Thread (() => {
					int n = (int)writeCount - (int)_ReadCount;
					rereadSucceed = _Rereader.Reread(stream, n);
				});
				thread.Start ();
			}

			if (_WriterCount != readCount) {
				if (!_Rewriter.Rewrite (stream, _WriterCount, readCount))
					return false;
			}

			if (thread != null) {
				thread.Join ();
				if (!rereadSucceed)
					return false;
			}

			setBaseStream (stream);
			return true;
		}

		private void setBaseStream(NetworkStream stream)
		{
			_BaseStream = stream;

			if (_ReadTimeout > 0)
				_BaseStream.ReadTimeout = this.ReadTimeout;

			if (_WriteTimeout > 0)
				_BaseStream.WriteTimeout = this.WriteTimeout;
		}

		public override void Flush ()
		{
			_WriteLock.WaitOne ();
			_ReconnLock.AcquireReaderLock (-1);
			try {
				_BaseStream.Flush ();
			} catch {
				if (!tryReconn())
					throw;
			} finally {
				_ReconnLock.ReleaseReaderLock ();
				_WriteLock.ReleaseMutex ();
			}
		}

		public override void Close ()
		{
			_Closed = true;
			_BaseStream.Close ();
		}

		public int ConnectTimeout {
			get;
			set;
		}

		private int _ReadTimeout;

		public override int ReadTimeout {
			get { return _ReadTimeout; }
			set {
				_ReadTimeout = value;
				if (_BaseStream != null)
					_BaseStream.ReadTimeout = value;
			}
		}

		private int _WriteTimeout;

		public override int WriteTimeout {
			get { return _WriteTimeout; }
			set {
				_WriteTimeout = value;
				if (_BaseStream != null)
					_BaseStream.WriteTimeout = value;
			}
		}
	}
}
