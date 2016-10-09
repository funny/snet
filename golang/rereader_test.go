package snet

import (
	"bytes"
	"encoding/hex"
	"math/rand"
	"sync"
	"testing"
)

func Test_Rereader(t *testing.T) {
	var (
		r rereader
		m sync.Mutex
		c = make(chan []byte, 100000)
	)

	go func() {
		for i := 0; i < 1000000; i++ {
			//println("i2 =", i)
			b := RandBytes(100)
			m.Lock()
			r.Reread(bytes.NewReader(b), len(b))
			m.Unlock()
			c <- b
		}
	}()

	for i := 0; i < 1000000; i++ {
		//println("i =", i)
		raw := <-c
		b := make([]byte, len(raw))
		for i, n, x := 0, len(raw), 0; n > 0; i, n = i+x, n-x {
			x = rand.Intn(n + 1)
			if x == 0 {
				continue
			}
			//println(i, n, x)
			m.Lock()
			r.Pull(b[i : i+x])
			m.Unlock()
		}
		if !bytes.Equal(b, raw) {
			t.Log("raw = ", hex.EncodeToString(raw))
			t.Log("b = ", hex.EncodeToString(b))
			t.Fatal("b != raw")
		}
	}
}
