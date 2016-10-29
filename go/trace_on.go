// +build snet_trace

package snet

import (
	"fmt"
	"log"
)

func (l *Listener) trace(format string, args ...interface{}) {
	log.Printf("Listener: "+format, args...)
}

func (c *Conn) trace(format string, args ...interface{}) {
	if c.listener == nil {
		format = fmt.Sprintf("Client conn %d: %s", c.id, format)
	} else {
		format = fmt.Sprintf("Server conn %d: %s", c.id, format)
	}
	log.Printf(format, args...)
}
