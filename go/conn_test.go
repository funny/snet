package snet

import (
	"bytes"
	"crypto/md5"
	"encoding/binary"
	"encoding/hex"
	"io"
	"math/rand"
	"net"
	"os"
	"strings"
	"sync"
	"testing"
	"time"

	dh64 "github.com/funny/crypto/dh64/go"
	"github.com/funny/utest"
)

type unstableListener struct {
	net.Listener
}

func (l *unstableListener) Accept() (net.Conn, error) {
	conn, err := l.Listener.Accept()
	if err != nil {
		return nil, err
	}
	return &unstableConn{Conn: conn}, nil
}

type unstableConn struct {
	net.Conn
	wn int
	rn int
}

func (c *unstableConn) Write(b []byte) (int, error) {
	if c.wn > 10 {
		if rand.Intn(10000) < 500 {
			c.Conn.Close()
		}
	} else {
		c.wn++
	}
	return c.Conn.Write(b)
}

func (c *unstableConn) Read(b []byte) (int, error) {
	if c.rn > 10 {
		if rand.Intn(10000) < 100 {
			c.Conn.Close()
		}
	} else {
		c.rn++
	}
	return c.Conn.Read(b)
}

func RandBytes(n int) []byte {
	n = rand.Intn(n) + 1
	b := make([]byte, n)
	for i := 0; i < n; i++ {
		b[i] = byte(rand.Intn(255))
	}
	return b
}

func ConnTest(t *testing.T, unstable, encrypt, reconn bool) {
	config := Config{
		EnableCrypt:        encrypt,
		HandshakeTimeout:   time.Second * 5,
		RewriterBufferSize: 1024,
		ReconnWaitTimeout:  time.Minute * 5,
	}

	listener, err := Listen(config, func() (net.Listener, error) {
		l, err := net.Listen("tcp", "0.0.0.0:0")
		if err != nil {
			return nil, err
		}
		if unstable {
			return &unstableListener{l}, nil
		}
		return l, nil
	})
	if err != nil {
		t.Fatalf("listen failed: %s", err.Error())
		return
	}

	var wg sync.WaitGroup
	wg.Add(1)
	go func() {
		conn, err := listener.Accept()
		if err != nil {
			t.Fatalf("accept failed: %s", err.Error())
			return
		}
		//if unstable {
		//	conn.(*Conn).base.(*unstableConn).wn = 11
		//}
		io.Copy(conn, conn)
		conn.Close()
		t.Log("copy exit")
		wg.Done()
	}()

	conn, err := Dial(config, func() (net.Conn, error) {
		conn, err := net.Dial("tcp", listener.Addr().String())
		if err != nil {
			return nil, err
		}
		if unstable {
			return &unstableConn{Conn: conn}, nil
		}
		return conn, nil
	})
	if err != nil {
		t.Fatalf("dial stable conn failed: %s", err.Error())
		return
	}
	defer conn.Close()

	t.Log(conn.LocalAddr())
	t.Log(conn.RemoteAddr())

	err = conn.SetDeadline(time.Time{})
	utest.IsNilNow(t, err)

	err = conn.SetReadDeadline(time.Time{})
	utest.IsNilNow(t, err)

	err = conn.SetWriteDeadline(time.Time{})
	utest.IsNilNow(t, err)

	conn.(*Conn).SetReconnWaitTimeout(config.ReconnWaitTimeout)

	time.Sleep(100 * time.Millisecond)
	for i := 0; i < 100000; i++ {
		b := RandBytes(100)
		c := b
		if encrypt {
			c = make([]byte, len(b))
			copy(c, b)
		}

		if _, err := conn.Write(b); err != nil {
			t.Fatalf("write failed: %s", err.Error())
			return
		}

		if reconn && i%100 == 0 {
			conn.(*Conn).TryReconn()
		}

		a := make([]byte, len(b))
		if _, err := io.ReadFull(conn, a); err != nil {
			t.Fatalf("read failed: %s", err.Error())
			return
		}

		if !bytes.Equal(a, c) {
			println("i =", i)
			println("a =", hex.EncodeToString(a))
			println("c =", hex.EncodeToString(c))
			t.Fatalf("a != c")
			return
		}
	}

	conn.Close()
	listener.Close()

	wg.Wait()
}

func Test_Stable_NoEncrypt(t *testing.T) {
	ConnTest(t, false, false, false)
}

func Test_Unstable_NoEncrypt(t *testing.T) {
	ConnTest(t, true, false, false)
}

func Test_Stable_Encrypt(t *testing.T) {
	ConnTest(t, false, true, false)
}

func Test_Unstable_Encrypt(t *testing.T) {
	ConnTest(t, true, true, false)
}

func Test_Stable_NoEncrypt_Reconn(t *testing.T) {
	ConnTest(t, false, false, true)
}

func Test_Unstable_NoEncrypt_Reconn(t *testing.T) {
	ConnTest(t, true, false, true)
}

func Test_Stable_Encrypt_Reconn(t *testing.T) {
	ConnTest(t, false, true, true)
}

func Test_Unstable_Encrypt_Reconn(t *testing.T) {
	ConnTest(t, true, true, true)
}

func reconnTest(t *testing.T, errorType int) {
	config := Config{
		EnableCrypt:        true,
		HandshakeTimeout:   time.Second * 5,
		RewriterBufferSize: 1024,
		ReconnWaitTimeout:  time.Minute * 5,
	}

	listener, err := Listen(config, func() (net.Listener, error) {
		l, err := net.Listen("tcp", "0.0.0.0:0")
		if err != nil {
			return nil, err
		}
		return l, nil
	})

	if err != nil {
		t.Fatalf("listen failed: %s", err.Error())
		return
	}

	var wg sync.WaitGroup
	wg.Add(1)
	go func() {
		conn, err := listener.Accept()
		if err != nil {
			t.Fatalf("accept failed: %s", err.Error())
			return
		}

		io.Copy(conn, conn)
		conn.Close()
		t.Log("copy exit")
		wg.Done()
	}()

	conn, err := Dial(config, func() (net.Conn, error) {
		conn, err := net.Dial("tcp", listener.Addr().String())
		if err != nil {
			return nil, err
		}
		return conn, nil
	})

	if err != nil {
		t.Fatalf("dial stable conn failed: %s", err.Error())
		return
	}
	defer conn.Close()

	b := RandBytes(100)

	if _, err := conn.Write(b); err != nil {
		t.Fatalf("write failed: %s", err.Error())
		return
	}

	a := make([]byte, len(b))
	if _, err := io.ReadFull(conn, a); err != nil {
		t.Fatalf("read failed: %s", err.Error())
		return
	}

	switch errorType {
	case 1:
		conn.(*Conn).writeCount += uint64(config.RewriterBufferSize) + 1
	case 2:
		conn.(*Conn).writeCount--
	case 3:
		conn.(*Conn).readCount++
	case 4:
		conn.(*Conn).id++
	case 5:
		conn.(*Conn).key[0] ^= byte(99)
	}
	conn.(*Conn).TryReconn()
	time.Sleep(100 * time.Millisecond)

	if _, err := conn.Write(b); err == nil {
		t.Fatalf("check has error")
		return
	}

	conn.Close()
	listener.Close()
	wg.Wait()
}

func Test_Reconn1(t *testing.T) {
	reconnTest(t, 1)
}

func Test_Reconn2(t *testing.T) {
	reconnTest(t, 2)
}

func Test_Reconn3(t *testing.T) {
	reconnTest(t, 3)
}

func Test_Reconn4(t *testing.T) {
	reconnTest(t, 4)
}

func Test_Reconn5(t *testing.T) {
	reconnTest(t, 5)
}

func handShakeTest(t *testing.T, errType int) {
	config := Config{
		EnableCrypt:        true,
		HandshakeTimeout:   time.Second * 5,
		RewriterBufferSize: 1024,
		ReconnWaitTimeout:  time.Minute * 5,
	}

	listener, err := Listen(config, func() (net.Listener, error) {
		l, err := net.Listen("tcp", "0.0.0.0:0")
		if err != nil {
			return nil, err
		}

		return l, nil
	})
	if err != nil {
		t.Fatalf("listen failed: %s", err.Error())
		return
	}

	go func() {
		conn, err := listener.Accept()
		if err != nil {
			if err != os.ErrInvalid {
				t.Fatalf("accept failed: %s", err.Error())
			}
			return
		}

		io.Copy(conn, conn)
		conn.Close()
		t.Log("copy exit")
	}()

	conn, err := net.Dial("tcp", listener.Addr().String())
	if err != nil {
		t.Fatalf("dial stable conn failed: %s", err.Error())
		return
	}
	defer conn.Close()

	var (
		preBuf [1]byte
		buf    [24]byte
		field1 = buf[0:8]
		field2 = buf[8:16]
		field3 = buf[16:24]
	)
	preBuf[0] = TYPE_NEWCONN
	if errType == 1 {
		conn.Close()
		return
	}
	// 测试错误连接类型
	if errType == 2 {
		preBuf[0] = byte(1)
	}

	if n, err := conn.Write(preBuf[:]); n != len(preBuf) || err != nil {
		t.Fatalf("write pre request failed: %s", err.Error())
	}
	// 测试不上传公钥
	if errType == 3 {
		conn.Close()
		return
	}

	privKey, pubKey := dh64.KeyPair()
	// 测试公钥不为0
	if errType == 4 {
		pubKey = 0
	}
	binary.LittleEndian.PutUint64(field1, pubKey)
	if n, err := conn.Write(field1); n != len(field1) || err != nil {
		if err == io.EOF {
			return
		}
		t.Fatalf("write pubkey failed: %s", err.Error())
	}

	if n, err := io.ReadFull(conn, buf[:]); n != len(buf) || err != nil {
		if err == io.EOF || strings.Contains(err.Error(), "connection reset by peer") {
			return
		}
		t.Fatalf("read pubkey failed: %s", err.Error())
	}

	srvPubKey := binary.LittleEndian.Uint64(field1)
	secret := dh64.Secret(privKey, srvPubKey)

	sconn, err := newConn(conn, 0, secret, config)
	if err != nil {
		t.Fatalf("new conn failed: %s", err.Error())
	}

	// 测试不上传二次握手响应
	if errType == 5 {
		conn.Close()
		return
	}

	// 二次握手
	sconn.trace("twice handshake")
	var buf2 [md5.Size]byte
	hash := md5.New()
	hash.Write(field3)
	hash.Write(sconn.key[:])
	copy(buf2[:], hash.Sum(nil))

	// 测试错误二次握手响应
	if errType == 6 {
		buf2[0] ^= byte(255)
	}
	if n, err := conn.Write(buf2[:]); n != len(buf2) || err != nil {
		if err == io.EOF {
			return
		}
		t.Fatalf("dial stable conn failed: %s", err.Error())
	}

	sconn.readCipher.XORKeyStream(field2, field2)
	sconn.id = binary.LittleEndian.Uint64(field2)

	sconn.Close()
	listener.Close()
}

func Test_Handshake1(t *testing.T) {
	handShakeTest(t, 1)
}

func Test_Handshake2(t *testing.T) {
	handShakeTest(t, 2)
}

func Test_Handshake3(t *testing.T) {
	handShakeTest(t, 3)
}

func Test_Handshake4(t *testing.T) {
	handShakeTest(t, 4)
}

func Test_Handshake5(t *testing.T) {
	handShakeTest(t, 5)
}

func Test_Handshake6(t *testing.T) {
	handShakeTest(t, 6)
}
