# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

<!--
## [Unreleased] - YYYY-MM-NN

### Added   
### Changed  
### Deprecated  
### Removed  
### Fixed  
### Security  
-->

## [Unreleased] - YYYY-MM-NN

## [2.6.1] - 2023-01-12
### Added   
- 增加OnDeadlink虚函数。

## [2.6.0] - 2022-10-29
### Added   
- 增加多线程说明
- 整理代码。增加扩展函数
- 增加snd_queueLock;
- 增加多线程Send安全。

### Changed  
- 使用 while (true) 代替递归。
- 使用内存池优化KcpIO
- 重命名RecvAsync(IBufferWriter<byte> writer, object options = null);增加一个异步接收方法;
### Removed  
- 删除过时代码

## [2.5.2] - 2022-10-14
 
### Changed  
- Log显示len和cmd
- Log显示len和cmd

## [2.5.1] - 2022-10-10

### Added   
- KcpSegment增加单元测试
- public int fastresend; 
- public int fastlimit;

## [2.5.0] - 2022-10-10

### Added   
- 增加注释。修复默认窗口值。
- 增加  throw new NotSupportedException($"分片数大于接收窗口，造成kcp阻塞冻结。frgCount:{newseg.frg + 1}  rcv_wnd:{rcv_wnd}");
- 提取Input接口
- 增加IKCP_LOG

### Changed  
- 使用共享数组优化List
- 统一使用Utc时间戳
### Deprecated  
### Removed  
### Fixed  
- 修复KcpIO接收过程中的死锁
### Security  

# [v2.4.1]
### Fixed
- 修复KcpIO，Recv异步不触发Bug。

# v2.4.0
### Fixed
- 修复多线程引起的空引用。https://github.com/KumoKyaku/KCP/issues/20
### Changed
- 将时间类型由DateTime改为DateTimeOffset.

# v2.3.0

- 增加SimpleKcpClient,增加简单Udp例子.


# v2.2.3

- 增加PoolSegManager实现，使用Seq对象池，解决内核调用效率低问题。

# v2.2.0

- 公开了KcpSegment
- KcpSegment泛型化，可由用户自定义高性能实现。

# v2.1.0

- 标准化KCPIO API，主要提供异步API
- 一个kcpio实现，没有严格优化和测试

# v2.0.0

- 将内存租用回调分离为单独的接口，并且不是必须的参数。  
  **`从旧版本升级代码不会报错，但是会失去原租用内存的效果，请手动修改。`**
- new: IKCP_FASTACK_LIMIT

# V1.3.0
- 修改了output函数接口参数。增加对Span的兼容性。
- 优化了数据拷贝。
  
# V1.2.0
- 支持IDispose；
- 修复了析构时内存泄漏；
- 为了防止误用，隐藏了KcpSegment;

# V1.1.0
- 支持多个目标框架；
- 支持多个目标框架单元测试；

