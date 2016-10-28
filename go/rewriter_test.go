package snet

import (
	"bytes"
	"encoding/hex"
	"math/rand"
	"testing"
)

type rewriterTester struct {
	r *rewriter
	t *testing.T
	b []byte
}

func (rt *rewriterTester) Write(b []byte) (int, error) {
	rt.b = append(rt.b, b...)
	return len(b), nil
}

func (rt *rewriterTester) Match(writeCount, readCount uint64, b []byte) {
	if !rt.r.Rewrite(rt, writeCount, readCount) {
		rt.t.FailNow()
		return
	}
	if !bytes.Equal(rt.b, b) {
		rt.t.Fatalf("wc = %d, rc = %d, rt.b = %v, b = %v", writeCount, readCount, rt.b, b)
		return
	}
	rt.b = rt.b[:0]
}

func Test_Rewriter1(t *testing.T) {
	writer := &rewriter{data: make([]byte, 5)}
	tester := &rewriterTester{writer, t, nil}

	writer.Push([]byte{0, 1, 2, 3})
	tester.Match(4, 0, []byte{0, 1, 2, 3})
	tester.Match(4, 1, []byte{1, 2, 3})
	tester.Match(4, 4, []byte{})

	writer.Push([]byte{4, 5, 6, 7})
	tester.Match(8, 3, []byte{3, 4, 5, 6, 7})
	tester.Match(8, 4, []byte{4, 5, 6, 7})
	tester.Match(8, 5, []byte{5, 6, 7})

	writer.Push([]byte{8, 9, 10, 11})
	tester.Match(12, 7, []byte{7, 8, 9, 10, 11})
	tester.Match(12, 8, []byte{8, 9, 10, 11})
}

func Test_Rewriter2(t *testing.T) {
	w := &rewriter{data: make([]byte, 1024)}

	var (
		writeCount uint64
		readCount  uint64
	)
	for i := 0; i < 1000000; i++ {
		a := RandBytes(100)
		w.Push(a)
		writeCount += uint64(len(a))

		b := make([]byte, len(a))
		for i, n, x := 0, len(a), 0; n > 0; i, n = i+x, n-x {
			x = rand.Intn(n + 1)
			if x == 0 {
				continue
			}
			buf := &ByteWriter{b[i : i+x], 0}
			if !w.Rewrite(buf, writeCount, readCount) {
				t.FailNow()
			}
			readCount += uint64(x)
		}

		if !bytes.Equal(a, b) {
			t.Log("a =", hex.EncodeToString(a))
			t.Log("b =", hex.EncodeToString(b))
			t.Fatal("a != b")
		}
	}
}

type ByteWriter struct {
	b []byte
	n int
}

func (w *ByteWriter) Write(b []byte) (int, error) {
	copy(w.b[w.n:], b)
	if x := len(w.b) - w.n; len(b) > x {
		w.n = len(w.b)
		return x, nil
	}
	w.n += len(b)
	return len(b), nil
}
