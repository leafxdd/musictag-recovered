# QQ 音乐 API 2001 缓解计划（2026-08）

## 目标

降低 QQ 音乐组合标签、歌词和封面搜索因 `req_0.code == 2001` 被业务风控拒绝的概率，避免同一进程内的重复请求和组合搜索回退继续放大压力。`2001` 是 HTTP 200 响应中的 QQ 业务码，不等同于网络异常。

## 已确认的本地风险

- `QqMusicTagProvider.SearchSongs` 当前使用精简的 `DoSearchForQQMusicDesktop` 请求。
- 一次组合标签搜索最多执行三趟查询，每趟最多包含首次请求加五次重试，理论上可产生 18 个 QQ 搜索请求。
- QQ provider 按搜索操作创建，现有闸门和缓存不是进程级共享。
- `QQMusic_Cookie` 目前直接作为 HTTP `Cookie` header 发送，尚未同步为 `comm/loginUin` 请求上下文。

## 分阶段方案

### 阶段 0：基线与可观测性

1. 保留现有 `2001` 业务错误分类和“组合查询遇到限流即停止后续回退”的行为。
2. 通过可注入传输和时钟覆盖成功、空结果、解析失败、网络失败、2001、取消等 characterization 场景。
3. 记录请求协调器的命中、等待、冷却和拒绝状态；日志只记录查询摘要和计数，不记录 Cookie、`authst`、`qm_keyst`。

### 阶段 1：进程级请求闸门、缓存和去重

1. 新增 QQ 进程级共享请求协调器，串行化 QQ 请求，并在请求之间保留保守间隔（首版 1.5 秒）。
2. 对成功且非空的搜索结果进行 10 分钟短缓存，缓存键包含规范化查询、结果数量和登录态摘要。
3. 对同一缓存键的并发搜索复用 in-flight 结果，避免多个窗口或 provider 实例重复请求。
4. 取消只影响调用方等待，不清理其他调用方仍在使用的成功缓存。

### 阶段 2：2001 全局冷却和退避

1. 任一 QQ 请求收到 2001 后，进程级进入 60 秒冷却；再次命中时延长到 120 秒并封顶。
2. 冷却期间不再发送新的 QQ 搜索请求，也不继续当前组合搜索的后续回退。
3. 同一轮不再用五次固定 4 秒等待反复撞击服务端；保留一次状态上报和可取消等待语义。
4. 其他 provider 不受 QQ 冷却影响。

### 阶段 3：登录态请求上下文

1. 从设置中的 Cookie 解析已有 `uin`、`loginUin`、`authst/qm_keyst` 等字段。
2. 在字段存在时同步到 `comm` 和请求体，并保留原始 Cookie header 兼容现有配置。
3. 区分完整登录态、不完整登录态和匿名态；失效时向 UI 提示，不输出敏感字段值。

### 阶段 3.1：歌词请求削峰和 Cookie 可用性检查

1. 对成功解析的 QQ 歌词按歌曲 ID、MID 和 Cookie 摘要缓存 30 分钟；同一歌曲的并发加载复用 in-flight 结果，缓存返回值使用独立对象，避免窗口间共享 UI 状态。
2. Cookie 保存前执行一次短超时 API 探测；明确登录失效业务码（`1000`、`104400`、`104401`）阻止保存并提示重新复制。限流、断网和未知响应只提示暂时无法确认，仍允许保存。
3. 搜索、歌词 QRC 和歌曲 ID 详情遇到上述失效码时停止 QQ 后续回退，并在搜索状态栏显示 Cookie 已过期；不把失效结果写入缓存。

### 阶段 4：Mobile/Session 协议评估

1. 录制 `DoSearchForQQMusicMobile`、完整 `comm` 和 `GetSession` 的低频请求/响应 fixture。
2. 先保留 Desktop 请求回退路径，再以多次低频人工验证结果决定默认协议。
3. 不采用 IP 轮换、设备指纹伪造或未经确认的复杂签名复制。

## 本次执行范围

本次已执行阶段 0 至阶段 3.1：新增共享协调器、接入 QQ 搜索和 QRC 歌词请求、补充 characterization、UI 冷却状态和登录态请求上下文，并增加歌词缓存、Cookie 在线探测和过期状态。阶段 4 仍单独保留，避免把尚未完成录制和低频验证的 Mobile/Session 协议变化混入确定性的请求削峰改动。

### 已落地实现

- `QqRequestCoordinator` 对生产 QQ provider 实例统一执行 1.5 秒最小请求间隔。
- 成功且非空的搜索结果按规范化查询、结果数量和 Cookie 摘要缓存 10 分钟；同一键的同步 in-flight 请求复用结果。
- 首次 `2001` 立即进入 60 秒冷却；冷却期间停止新的 QQ 搜索、组合回退和 QRC/legacy 歌词请求；连续再次命中时延长至 120 秒。
- 搜索状态增加 `CoolingDown`，三个共用搜索窗口显示 QQ 冷却剩余秒数，归零后转为普通错误状态。
- Cookie 中的 `uin/loginUin/authst/qm_keyst/qqmusic_key/tmeLoginType`（仅实际存在的字段）同步到搜索请求的 `loginUin/comm`，原始 Cookie header 保持兼容。
- 设置页保存非空 Cookie 时校验账号字段（`uin/Uin/p_uin/euin`）和鉴权字段（`authst/qm_keyst/qqmusic_key`）；`loginUin` 是请求体字段，不作为 Cookie 缺失项。
- 成功 QQ 歌词按歌曲 ID、MID 和 Cookie 摘要缓存 30 分钟，并复用同一歌曲的并发 in-flight 加载；缓存只接受至少有原文或译文的解析结果，不缓存空结果、解析失败或限流结果。
- 设置页保存非空 Cookie 时发送一次 5 秒超时探测；`1000/104400/104401` 显示 Cookie 过期并阻止保存，`2001`、网络错误和未知响应显示提示但保留设置。查询和 ID 详情遇到失效码时显示专用状态并停止 QQ 回退。
- 测试 provider 显式关闭真实协调器等待，避免 characterization 依赖墙钟时间；生产类型默认始终启用。

当前 characterization 结果为 `978 passed, 0 failed`；本轮已完成测试项目构建和 characterization，完整 `Verify-Build.ps1 -RunSmokeTests` 仍需在提交前执行。上述缓存、探测解析和协调器测试使用录制响应，不代表真实 QQ 端已稳定放行。

## 验证门槛

```powershell
.\scripts\Verify-Build.ps1 -RunSmokeTests
git diff --check
codegraph sync
```

还需低频人工验证直连/代理、匿名/有效 Cookie、首次搜索/重复打开窗口，以及 2001 冷却期间的 UI 和其他 provider 行为。公开项目中的参数和缓存模式只能降低概率，不能保证 QQ 永不返回 2001。
