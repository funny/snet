package snet

import (
	"bytes"
	"crypto/md5"
	"crypto/rand"
	"crypto/rc4"
	"encoding/binary"
	"io"
	"net"
	"sync"
	"time"

	dh64 "github.com/funny/crypto/dh64/go"
)

var _ net.Conn = &Conn{}

type Config struct {
	EnableCrypt        bool
	HandshakeTimeout   time.Duration
	RewriterBufferSize int
	ReconnWaitTimeout  time.Duration
}

type Dialer func() (net.Conn, error)

type Conn struct {
	base     net.Conn
	id       uint64
	listener *Listener
	dialer   Dialer

	key         [8]byte
	enableCrypt bool

	closed    bool
	closeChan chan struct{}
	closeOnce sync.Once

	writeMutex  sync.Mutex
	writeCipher *rc4.Cipher

	readMutex  sync.Mutex
	readCipher *rc4.Cipher

	reconnMutex       sync.RWMutex
	reconnOpMutex     sync.Mutex
	readWaiting       bool
	writeWaiting      bool
	readWaitChan      chan struct{}
	writeWaitChan     chan struct{}
	reconnWaitTimeout time.Duration

	rewriter   rewriter
	rereader   rereader
	readCount  uint64
	writeCount uint64
}

func Dial(config Config, dialer Dialer) (net.Conn, error) {
	conn, err := dialer()
	if err != nil {
		return nil, err
	}

	var (
		preBuf [1]byte
		buf    [24]byte
		field1 = buf[0:8]
		field2 = buf[8:16]
		field3 = buf[16:24]
	)
	preBuf[0] = TYPE_NEWCONN
	if _, err := conn.Write(preBuf[:]); err != nil {
		return nil, err
	}

	privKey, pubKey := dh64.KeyPair()
	binary.LittleEndian.PutUint64(field1, pubKey)
	if _, err := conn.Write(field1); err != nil {
		return nil, err
	}

	if _, err := io.ReadFull(conn, buf[:]); err != nil {
		return nil, err
	}

	srvPubKey := binary.LittleEndian.Uint64(field1)
	secret := dh64.Secret(privKey, srvPubKey)

	sconn, err := newConn(conn, 0, secret, config)
	if err != nil {
		return nil, err
	}

	// 二次握手
	sconn.trace("twice handshake")
	var buf2 [md5.Size]byte
	hash := md5.New()
	hash.Write(field3)
	hash.Write(sconn.key[:])
	copy(buf2[:], hash.Sum(nil))
	if _, err := conn.Write(buf2[:]); err != nil {
		return nil, err
	}

	sconn.readCipher.XORKeyStream(field2, field2)
	sconn.id = binary.LittleEndian.Uint64(field2)
	sconn.dialer = dialer
	return sconn, nil
}

func newConn(base net.Conn, id, secret uint64, config Config) (conn *Conn, err error) {
	conn = &Conn{
		base:              base,
		id:                id,
		enableCrypt:       config.EnableCrypt,
		reconnWaitTimeout: config.ReconnWaitTimeout,
		closeChan:         make(chan struct{}),
		readWaitChan:      make(chan struct{}),
		writeWaitChan:     make(chan struct{}),
		rewriter: rewriter{
			data: make([]byte, config.RewriterBufferSize),
		},
	}

	binary.LittleEndian.PutUint64(conn.key[:], secret)

	conn.writeCipher, err = rc4.NewCipher(conn.key[:])
	if err != nil {
		return nil, err
	}

	conn.readCipher, err = rc4.NewCipher(conn.key[:])
	if err != nil {
		return nil, err
	}

	return conn, nil
}

func (c *Conn) WrapBaseForTest(wrap func(net.Conn) net.Conn) {
	c.base = wrap(c.base)
}

func (c *Conn) RemoteAddr() net.Addr {
	c.reconnMutex.RLock()
	defer c.reconnMutex.RUnlock()
	return c.base.RemoteAddr()
}

func (c *Conn) LocalAddr() net.Addr {
	c.reconnMutex.RLock()
	defer c.reconnMutex.RUnlock()
	return c.base.LocalAddr()
}

func (c *Conn) SetDeadline(t time.Time) error {
	c.reconnMutex.RLock()
	defer c.reconnMutex.RUnlock()
	return c.base.SetDeadline(t)
}

func (c *Conn) SetReadDeadline(t time.Time) error {
	c.reconnMutex.RLock()
	defer c.reconnMutex.RUnlock()
	return c.base.SetReadDeadline(t)
}

func (c *Conn) SetWriteDeadline(t time.Time) error {
	c.reconnMutex.RLock()
	defer c.reconnMutex.RUnlock()
	return c.base.SetWriteDeadline(t)
}

func (c *Conn) SetReconnWaitTimeout(d time.Duration) {
	c.reconnWaitTimeout = d
}

func (c *Conn) Close() error {
	c.trace("Close()")
	c.closeOnce.Do(func() {
		c.closed = true
		if c.listener != nil {
			c.listener.delConn(c.id)
		}
		close(c.closeChan)
	})
	return c.base.Close()
}

func (c *Conn) TryReconn() {
	if c.listener == nil {
		c.reconnMutex.RLock()
		base := c.base
		c.reconnMutex.RUnlock()
		go c.tryReconn(base)
	}
}

func (c *Conn) Read(b []byte) (n int, err error) {
	c.trace("Read(%d)", len(b))
	if len(b) == 0 {
		return
	}

	c.trace("Read() wait write")
	c.readMutex.Lock()
	c.trace("Read() wait reconn")
	c.reconnMutex.RLock()
	c.readWaiting = true

	defer func() {
		c.readWaiting = false
		c.reconnMutex.RUnlock()
		c.readMutex.Unlock()
	}()

	for {
		n, err = c.rereader.Pull(b), nil
		c.trace("read from queue, n = %d", n)
		if n > 0 {
			break
		}

		base := c.base
		n, err = base.Read(b[n:])
		if err == nil {
			c.trace("read from conn, n = %d", n)
			break
		}
		base.Close()

		if c.listener == nil {
			go c.tryReconn(base)
		}

		if !c.waitReconn('r', c.readWaitChan) {
			break
		}
	}

	if err == nil {
		if c.enableCrypt {
			c.readCipher.XORKeyStream(b[:n], b[:n])
		}
		c.readCount += uint64(n)
	}

	c.trace("Read(), n = %d, err = %v", n, err)
	return
}

func (c *Conn) Write(b []byte) (n int, err error) {
	c.trace("Write(%d)", len(b))
	if len(b) == 0 {
		return
	}

	c.trace("Write() wait write")
	c.writeMutex.Lock()
	c.trace("Write() wait reconn")
	c.reconnMutex.RLock()
	c.writeWaiting = true

	defer func() {
		c.writeWaiting = false
		c.reconnMutex.RUnlock()
		c.writeMutex.Unlock()
	}()

	if c.enableCrypt {
		c.writeCipher.XORKeyStream(b, b)
	}

	c.rewriter.Push(b)
	c.writeCount += uint64(len(b))

	base := c.base
	n, err = base.Write(b)
	if err == nil {
		return
	}
	base.Close()

	if c.listener == nil {
		go c.tryReconn(base)
	}

	if c.waitReconn('w', c.writeWaitChan) {
		n, err = len(b), nil
	}
	return
}

func (c *Conn) waitReconn(who byte, waitChan chan struct{}) (done bool) {
	c.trace("waitReconn('%c', \"%s\")", who, c.reconnWaitTimeout)

	timeout := time.NewTimer(c.reconnWaitTimeout)
	defer timeout.Stop()

	c.reconnMutex.RUnlock()
	defer func() {
		c.reconnMutex.RLock()
		if done {
			<-waitChan
			c.trace("waitReconn('%c', \"%s\") done", who, c.reconnWaitTimeout)
		}
	}()

	var lsnCloseChan chan struct{}
	if c.listener == nil {
		lsnCloseChan = make(chan struct{})
	} else {
		lsnCloseChan = c.listener.closeChan
	}

	select {
	case <-waitChan:
		done = true
		c.trace("waitReconn('%c', \"%s\") wake up", who, c.reconnWaitTimeout)
		return
	case <-c.closeChan:
		c.trace("waitReconn('%c', \"%s\") closed", who, c.reconnWaitTimeout)
		return
	case <-timeout.C:
		c.trace("waitReconn('%c', \"%s\") timeout", who, c.reconnWaitTimeout)
		c.Close()
		return
	case <-lsnCloseChan:
		c.trace("waitReconn('%c', \"%s\") listener closed", who, c.reconnWaitTimeout)
		return
	}
}

func (c *Conn) handleReconn(conn net.Conn, writeCount, readCount uint64) {
	var done bool

	c.trace("handleReconn() wait handleReconn()")
	c.reconnOpMutex.Lock()
	defer c.reconnOpMutex.Unlock()

	c.base.Close()
	c.trace("handleReconn() wait Read() or Write()")
	c.reconnMutex.Lock()
	readWaiting := c.readWaiting
	writeWaiting := c.writeWaiting
	defer func() {
		c.reconnMutex.Unlock()
		if done {
			c.wakeUp(readWaiting, writeWaiting)
		} else {
			conn.Close()
		}
	}()
	c.trace("handleReconn() begin")
	var (
		buf    [24]byte
		field1 = buf[0:8]
		field2 = buf[8:16]
		field3 = buf[16:24]
	)

	binary.LittleEndian.PutUint64(field1, c.writeCount)
	binary.LittleEndian.PutUint64(field2, c.readCount)
	rand.Read(field3)
	if _, err := conn.Write(buf[:]); err != nil {
		c.trace("reconn response failed")
		conn.Close()
		return
	}

	// 二次握手
	c.trace("reconn check")
	var buf2 [16]byte
	if _, err := io.ReadFull(conn, buf2[:]); err != nil {
		c.trace("read reconn check failed: %s", err)
		conn.Close()
		return
	}

	hash := md5.New()
	hash.Write(field3)
	hash.Write(c.key[:])
	md5sum := hash.Sum(nil)
	if !bytes.Equal(buf2[:], md5sum) {
		c.trace("reconn check not equals: %x, %x", buf2[:], md5sum)
		conn.Close()
		return
	}

	done = c.doReconn(conn, writeCount, readCount)
}

func (c *Conn) tryReconn(badConn net.Conn) {
	var done bool

	c.trace("tryReconn() wait tryReconn()")
	c.reconnOpMutex.Lock()
	defer c.reconnOpMutex.Unlock()

	c.trace("tryReconn() wait Read() or Write()")
	badConn.Close()
	c.reconnMutex.Lock()
	readWaiting := c.readWaiting
	writeWaiting := c.writeWaiting
	defer func() {
		c.reconnMutex.Unlock()
		if done {
			c.wakeUp(readWaiting, writeWaiting)
		}
	}()
	c.trace("tryReconn() begin")

	if badConn != c.base {
		c.trace("badConn != c.base")
		return
	}

	var (
		preBuf [1]byte
		buf    [24 + md5.Size]byte
		buf2   [24]byte
		buf3   [md5.Size]byte
	)

	preBuf[0] = TYPE_RECONN
	binary.LittleEndian.PutUint64(buf[0:8], c.id)
	binary.LittleEndian.PutUint64(buf[8:16], c.writeCount)
	binary.LittleEndian.PutUint64(buf[16:24], c.readCount)
	hash := md5.New()
	hash.Write(buf[0:24])
	hash.Write(c.key[:])
	copy(buf[24:], hash.Sum(nil))

	// 尝试重连
	for i := 0; !c.closed; i++ {
		if i > 0 {
			time.Sleep(time.Second * 3)

			c.trace("reconn dial")
			conn, err := c.dialer()
			if err != nil {
				c.trace("dial failed: %v", err)
				continue
			}

			c.trace("send reconn pre request")
			if _, err = conn.Write(preBuf[:]); err != nil {
				c.trace("pre write failed: %v", err)
				conn.Close()
				continue
			}

			c.trace("send reconn request")
			if _, err = conn.Write(buf[:]); err != nil {
				c.trace("write failed: %v", err)
				conn.Close()
				continue
			}

			c.trace("wait reconn response")
			if _, err = io.ReadFull(conn, buf2[:]); err != nil {
				c.trace("read failed: %v", err)
				conn.Close()
				continue
			}
			writeCount := binary.LittleEndian.Uint64(buf2[0:8])
			readCount := binary.LittleEndian.Uint64(buf2[8:16])
			challengeCode := binary.LittleEndian.Uint64(buf2[16:24])
			if writeCount == 0 && readCount == 0 && challengeCode == 0 {
				c.trace("Data corruption, cannot be reconnected")
				conn.Close()
				c.Close()
				break
			}

			c.trace("reconn check")
			hash := md5.New()
			hash.Write(buf2[16:24])
			hash.Write(c.key[:])
			copy(buf3[:], hash.Sum(nil))
			if _, err = conn.Write(buf3[:]); err != nil {
				c.trace("reconn check write failed: %v", err)
				conn.Close()
				continue
			}

			if writeCount < c.readCount {
				c.trace("writeCount < c.readCount")
				conn.Close()
				c.Close()
				break
			}

			if c.writeCount < readCount {
				c.trace("c.writeCount < readCount")
				conn.Close()
				c.Close()
				break
			}

			if c.doReconn(conn, writeCount, readCount) {
				done = true
				break
			}
			conn.Close()
		}
	}
}

func (c *Conn) doReconn(conn net.Conn, writeCount, readCount uint64) bool {
	c.trace(
		"doReconn(\"%s\", %d, %d), c.writeCount = %d, c.readCount = %d",
		conn.RemoteAddr(), writeCount, readCount, c.writeCount, c.readCount,
	)

	rereadWaitChan := make(chan bool)
	if writeCount != c.readCount {
		go func() {
			n := int(writeCount) - int(c.readCount)
			c.trace(
				"reread, writeCount = %d, c.readCount = %d, n = %d",
				writeCount, c.readCount, n,
			)
			rereadWaitChan <- c.rereader.Reread(conn, n)
		}()
	}

	if c.writeCount != readCount {
		c.trace(
			"rewrite, c.writeCount = %d, readCount = %d, n = %d",
			c.writeCount, readCount, c.writeCount-readCount,
		)
		if !c.rewriter.Rewrite(conn, c.writeCount, readCount) {
			c.trace("rewrite failed")
			return false
		}
		c.trace("rewrite done")
	}

	if writeCount != c.readCount {
		c.trace("reread wait")
		if !<-rereadWaitChan {
			c.trace("reread failed")
			return false
		}
		c.trace("reread done")
	}

	c.base = conn
	return true
}

func (c *Conn) wakeUp(readWaiting, writeWaiting bool) {
	if readWaiting {
		c.trace("continue read")
		// make sure reader take over reconnMutex
		for i := 0; i < 2; i++ {
			select {
			case c.readWaitChan <- struct{}{}:
			case <-c.closeChan:
				c.trace("continue read closed")
				return
			}
		}
		c.trace("continue read done")
	}

	if writeWaiting {
		c.trace("continue write")
		// make sure writer take over reconnMutex
		for i := 0; i < 2; i++ {
			select {
			case c.writeWaitChan <- struct{}{}:
			case <-c.closeChan:
				c.trace("continue write closed")
				return
			}
		}
		c.trace("continue write done")
	}
}
