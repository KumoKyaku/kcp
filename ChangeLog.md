<!--
## [Unreleased]

### Fixed

### Changed

### Added
-->

# [Unreleased]
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

