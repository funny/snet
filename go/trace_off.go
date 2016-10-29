// +build !snet_trace

package snet

func (l *Listener) trace(format string, args ...interface{}) {
}

func (c *Conn) trace(format string, args ...interface{}) {
}
