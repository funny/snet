package snet

import (
	"io"
)

type rewriter struct {
	data   []byte
	head   int
	length int
}

func (r *rewriter) Push(b []byte) {
	if len(b) >= len(r.data) {
		drop := len(b) - len(r.data)
		copy(r.data, b[drop:])
		r.head, r.length = 0, len(r.data)
		return
	}

	size := copy(r.data[r.head:], b)

	remain := len(b) - size

	if remain == 0 {
		r.head += size
		if r.head == len(r.data) {
			r.head = 0
		}

		if r.length != len(r.data) {
			r.length += len(r.data)
		}
	} else {
		r.head = copy(r.data, b[size:])
		if r.length != len(r.data) {
			r.length = len(r.data)
		}
	}
}

func (r *rewriter) Rewrite(w io.Writer, writeCount, readCount uint64) bool {
	n := int(writeCount - readCount)

	switch {
	case n == 0:
		return true
	case n < 0 || n > r.length:
		return false
	case n <= r.head:
		_, err := w.Write(r.data[r.head-n : r.head])
		return err == nil
	}

	offset := r.head - n + len(r.data)
	if _, err := w.Write(r.data[offset:]); err != nil {
		return false
	}

	_, err := w.Write(r.data[:r.head])
	return err == nil
}
