# KCP C#版。

支持目标框架:  
- dotnetstandard2.0
- dotnetstandard1.1

开箱即用。也可以使用Nuget 搜索KCP。

# 链接：

c: skywind3000 [KCP](https://github.com/skywind3000/kcp)  
go: xtaci [kcp-go](https://github.com/xtaci/kcp-go)  

# 用法：

请参考C版本文档。

# 说明：

- 内部使用了unsafe代码和非托管内存，不会对gc造成压力。

- 对于output回调和TryRecv函数。使用RentBuffer回调，从外部分配内存。请参考[IMemoryOwner](https://docs.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines)用法。
- 支持`Span<byte>`

# 测试：
[[已修复]~~同一个进程两个Kcp echo测试，至少使用3个线程，否则可能死锁。~~](Image/deadlock.jpg)

在UnitTestProject1路径下执行 dotnet test 可进行多框架测试。（需要安装notnetcoreSDK）

# 相对C版的一些变化：

| 差异变化         | C版            | C#版                                                  |
| ---------------- | -------------- | ----------------------------------------------------- |
| 数据结构         |                |                                                       |
| acklist          | 数组           | ConcurrentQueue                                       |
| snd_queue        | 双向链表       | ConcurrentQueue                                       |
| snd_buf          | 双向链表       | LinkedList                                            |
| rcv_buf          | 双向链表       | LinkedList                                            |
| rcv_queue        | 双向链表       | List                                                  |
| --------------   | -------------- | --------------                                        |
| 回调函数         |                | 增加了RentBuffer回调，当KCP需要时可以从外部申请内存。 |
| 多线程           |                | 增加了线程安全。                                      |
| 流模式           |                | 由于数据结构变动，移除了流模式。                      |
| interval最小间隔 | 10ms           | 0ms(在特殊形况下允许CPU满负荷运转)                    |
| --------------   | -------------- | --------------                                        |
| API变动          |                |                                                       |
|                  |                | 增加大小端编码设置。默认小端编码。                    |
|                  |                | 增加TryRecv函数，当可以Recv时只peeksize一次。         |
|                  | ikcp_ack_push  | 删除了此函数（已内联）                                |
|                  | ikcp_ack_get   | 删除了此函数（已内联）                                |
