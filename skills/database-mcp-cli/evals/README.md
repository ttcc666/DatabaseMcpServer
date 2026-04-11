# database-mcp-cli eval notes

这份 skill 的评测重点不是“能不能随便拼出一条命令”，而是以下 4 类能力：

1. **CLI 契约理解**
- 是否真的使用 `DatabaseMcpServer tool ...`
- 是否理解 `stdout` / `stderr` / exit code
- 是否知道 `get_database_config` 这类输出不一定有 `success`

2. **PowerShell 传参稳定性**
- SQL / JSON 是否用适合 PowerShell 的 quoting
- `batch_execute_commands` / `queries-json` / `parameters` 是否会因为转义写错
- `add_default_value` 这种需要 SQL 字面量的参数是否正确表达

3. **安全与隔离**
- 是否优先只读
- 是否对写操作加 `--yes`
- 是否在“测试所有 tool”时使用唯一前缀的临时对象
- 是否在结尾做清理

4. **诊断质量**
- 是否把 CLI 错误和数据库/驱动错误区分开
- 是否能识别 SQL Server `Encrypt=True` 的 TLS/驱动问题
- 是否在对象不存在时先回到 schema 检查，而不是瞎猜

## 建议的 benchmark 观察点

- **Pass/Fail 之外**，重点看输出有没有误导用户
- 对同一类失败，skill 是否稳定给出相同分类
- 是否会把需要 `--yes` 的情况误报成数据库异常
- 是否会给出不适用于 PowerShell 的命令格式

## 建议新增 assertions 的方向

- 输出里必须出现 `DatabaseMcpServer tool`
- 输出里必须包含 `--config`
- 涉及写操作时必须包含 `--yes`
- SQL Server 默认值示例必须包含 `'''active'''` 这类字面量传法
- SQL Server 加密诊断必须先解释 `Encrypt=True` 的错误，再提 `Encrypt=False`
