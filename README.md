# KCP C#版。
开箱即用。也可以使用Nuget 搜索KCP。

## Feature：

- 异步API标准接口 IKcpIO.cs
  - ValueTask Recv(IBufferWriter<byte> writer, object option = null);
  - ValueTask Output(IBufferWriter<byte> writer, object option = null);
  - 附带一个基本实现。KcpIO.cs
- kcpSegment泛型化，可由用户自定义高性能实现。
  - `KcpCore<Segment>`  where Segment : IKcpSegment
  - `KcpIO<Segment>` : `KcpCore<Segment>`, IKcpIO  where Segment : IKcpSegment
  - `Kcp<Segment>` : `KcpCore<Segment>` where Segment:IKcpSegment

## 链接：

c: skywind3000 [KCP](https://github.com/skywind3000/kcp)  
go: xtaci [kcp-go](https://github.com/xtaci/kcp-go)  

## 说明：

- 内部使用了unsafe代码和非托管内存，不会对gc造成压力。
- 支持用户自定义内存管理方式,如果不想使用unsafe模式,可以使用内存池.
- 对于output回调和TryRecv函数。使用RentBuffer回调，从外部分配内存。请参考[IMemoryOwner](https://docs.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines)用法。
- 支持`Span<byte>`

## 线程安全
简单的说：  
不能在线程1调用Recv/Update时，线程2也在调用Recv/Update。  
可以在线程1调用Send/Input时，线程2也在调用Send/Input。

- 可以在不同线程同时调用Update，Recv，Send，Input方法。  
- 可以在任意多线程同时调用Send 和 Input。  
- 但`不可以`多个线程同时Recv 和 Update。  
  同名方法仅支持一个线程同时调用，否则会导致多线程错误。  

## 测试：
[[已修复]~~同一个进程两个Kcp echo测试，至少使用3个线程，否则可能死锁。~~](Image/deadlock.jpg)

在UnitTestProject1路径下执行 dotnet test 可进行多框架测试。（需要安装dotnetcoreSDK）

## 相对C版的一些变化：

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


---
---
# 赞助链接

![支付宝](https://github.com/KumoKyaku/KumoKyaku.github.io/blob/develop/source/_posts/%E5%9B%BE%E5%BA%8A/alipay.png)



