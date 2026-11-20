# 按功能组织后端目录

在已完成的三程序集架构上继续整理目录，直接执行用户授权的模块归档，不使用子代理。

## 范围与不变量

- Core / Infrastructure 移除项目级 Models、Interfaces、Helpers、Services、Operations 和 Strategies 分类，按 Configuration、Connections、Execution、Sql、Mongo、Schema 等功能归组。
- 数据库策略归入 Providers 下的数据库子目录；Host 工具和测试按对应模块组织。
- 仅移动同一项目内的文件；保留源码字节、类型名、命名空间、程序集、项目引用、工具契约、配置及运行行为。
- 保留本轮之前全部未提交修改与暂存状态；不升级依赖，不提交或推送。

## 执行清单

- [x] 检查目录、类型职责、源码路径依赖和 SDK 默认编译项。
- [x] 记录迁移前 194 个源码及项目文件的哈希，生成明确路径映射。
- [x] 移动 161 个文件，逐个核对 SHA-256。
- [x] 更新 AGENTS.md 和架构文档的模块归属规则。
- [x] 清理移动后空目录：用户执行清理命令后复核，14 个旧目录均已不存在；194 个源码/项目文件内容哈希保持一致，4 个项目引用均有效。
- [x] 验证完整源码清单、双框架构建与非容器回归、Git 差异。

## 验证

迁移前本会话最近一次双框架构建零警告、零错误，非容器回归 net9 / net10 各 449 项通过。本次先核对全部源码字节和编译文件清单，再运行：

```powershell
dotnet build 'DatabaseMcpServer.slnx' -c 'Release'
dotnet test 'DatabaseMcpServer.slnx' -c 'Release' --no-build --filter 'Category!=Integration&Category!=MongoIntegration'
git diff --check
```

移动映射及验证输出位于忽略的 `artifacts/module-layout/`。文件哈希相同且归属项目相同，容器和包安装仍沿用上一轮行为验证结果，本轮不重复创建数据库容器。

## 验收结果

- 移动 Core 29 个、Infrastructure 76 个、Host 工具 7 个、测试 49 个文件，共 161 个。
- 整理前后均为 194 个源码/项目文件，全部 SHA-256 一致且归属项目相同：`content-verification.json`。
- Release 双框架构建：0 warnings / 0 errors，日志 `build.log`。
- 非容器回归：net9 / net10 各 449 passed，0 failed / skipped，日志 `tests.log`。
- 与 `ea9f3c5` 对比：62 个工具声明、27 项确认策略一致，记录 `tool-contracts.json`。
- `git diff --check` 通过；暂存区为空；未提交或推送。

## 回退

需要回退时按 `artifacts/module-layout/moves.json` 反向移动文件，先确认目的路径没有新内容；不要使用 git reset / checkout 覆盖尚未提交的架构改造。文档仅回退本次目录归档说明。
