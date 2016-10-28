package snet

import (
	"io"
)

type rereader struct {
	head *rereadData
	tail *rereadData
}

type rereadData struct {
	Data []byte
	next *rereadData
}

func (r *rereader) Pull(b []byte) (n int) {
	if r.head != nil {
		copy(b, r.head.Data)
		if len(r.head.Data) > len(b) {
			r.head.Data = r.head.Data[len(b):]
			n = len(b)
			return
		}
		n = len(r.head.Data)
		r.head = r.head.next
		if r.head == nil {
			r.tail = nil
		}
	}
	return
}

func (r *rereader) Reread(rd io.Reader, n int) bool {
	b := make([]byte, n)
	if _, err := io.ReadFull(rd, b); err != nil {
		return false
	}
	data := &rereadData{b, nil}
	if r.head == nil {
		r.head = data
	} else {
		r.tail.next = data
	}
	r.tail = data
	return true
}

func (r *rereader) Rollback() {
	if r.head == r.tail {
		r.head = nil
		r.tail = nil
		return
	}

	for item := r.head; item != nil; item = item.next {
		if item.next == r.tail {
			r.tail = item
			item.next = nil
			break
		}
	}
}
