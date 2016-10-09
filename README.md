介绍
====

[![Go Report Card](https://goreportcard.com/badge/github.com/funny/snet)](https://goreportcard.com/report/github.com/funny/snet)
[![Build Status](https://travis-ci.org/funny/snet.svg?branch=master)](https://travis-ci.org/funny/snet)
[![codecov](https://codecov.io/gh/funny/snet/branch/master/graph/badge.svg)](https://codecov.io/gh/funny/snet)
[![GoDoc](https://img.shields.io/badge/api-reference-blue.svg)](https://godoc.org/github.com/funny/snet/golang)

本项目在TCP/IP协议之上构建了一套支持重连和加密的流式网络通讯协议。

此协议的实现目的主要是提升长连接型应用在移动终端上的连接稳定性。

以期在可能的情况下尽量保证用户体验的连贯性，同时又不需要对已有代码做大量的修改。

协议
====

基本流程：

+ 客户端连接服务端时，协议采用DH密钥交换算法和服务端之间协商出一个通讯密钥
+ 在后续的通讯过程中，双方使用这个密钥对通讯内容进行RC4流式加密
+ 通讯双方，均在本地缓存一定量的历史数据，并记录已接收和已发送的字节数
+ 当底层TCP/IP连接意外断开时，客户端将新建一个连接并尝试重连，服务端将等待重连
+ 当新的连接创建成功，客户端和服务端之间互发已接收和已发送的字节数
+ 客户端和服务端各自比对双方的收发字节数来重传数据
+ 重连过程中，服务端使用之前协商的通讯密钥验证客户端的身份合法性

新建连接，上行：

+ 新建连接时，客户端发送16个字节的请求
+ 消息前8个字节全0
+ 消息后8个字节为DH密钥交换用的公钥
+ 消息结构：

	```
	+---------+------------+
	| Conn ID | Public Key |
	+---------+------------+
	   8 byte    8 byte
	```

新建连接，下行：

+ 当服务端收到新建连接请求后，下发16个字节的握手响应
+ 消息前8个字节为DH密钥交换用的公钥
+ 消息后8个字节为加密后的连接ID，加密所需密钥通过DH密钥交换算法计算得出
+ 消息结构：

	```
	+------------+-----------------+
	| Public Key | Crypted Conn ID |
	+------------+-----------------+
	    8 byte         8 byte
	```

重连，上行：

+ 当客户端尝试重连时，新建一个TCP/IP连接，并发送40个字节的重连请求
+ 消息前8个字节为连接ID
+ 消息的[8, 16)字节为客户端已发送字节数
+ 消息第[16, 24)字节为客户端已接收字节数
+ 消息第[24, 40)字节为消息前24个字节加通讯密钥计算得出的MD5哈希值
+ 消息结构：

	```
	+---------+-------------+------------+---------+
	| Conn ID | Write Count | Read Count |   MD5   |
	+---------+-------------+------------+---------+
	   8 byte     8 byte        8 byte     16 byte
	```

重连，下行：

+ 当服务端接收到重连请求时，对连接的合法性进行验证
+ 验证失败立即断开连接
+ 验证成功则下发16个字节的重连响应
+ 消息前8个字节为服务端已发送字节数
+ 消息后8个字节为服务端已接收字节数
+ 紧接着服务端立即下发需要重传的数据
+ 客户端在收到重连响应后，比较收发字节数差值来读取服务端下发的重传数据
+ 消息结构：

	```
	+-------------+------------+
	| Write Count | Read Count |
	+-------------+------------+
	     8 byte       8 byte
	```

实现
====

本协议目前有以下编程语言的实现：

+ [Go版，可直接替代net.Conn，迁移成本极低](https://github.com/funny/snet/tree/master/golang)
+ [C#版，可直接替代Stream，迁移成本极低](https://github.com/funny/snet/tree/master/csharp)

资料
=======

+ [在移动网络上创建更稳定的连接](http://blog.codingnow.com/2014/02/connection_reuse.html) by [云风](https://github.com/cloudwu)
+ [迪菲－赫尔曼密钥交换](https://zh.wikipedia.org/wiki/%E8%BF%AA%E8%8F%B2%EF%BC%8D%E8%B5%AB%E5%B0%94%E6%9B%BC%E5%AF%86%E9%92%A5%E4%BA%A4%E6%8D%A2)

TODO
====

还需完善的部分：

+ 自定义加密算法
+ 重连失败的响应