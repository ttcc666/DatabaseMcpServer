# DatabaseMcpServer CLI Troubleshooting

## 1. 缺少 `--yes`

现象：

```text
tool 'execute_command' 需要显式确认。请追加 '--yes'。
```

结论：

- 这是 CLI 使用错误
- 不是数据库连接错误

处理：

- 给高风险命令追加 `--yes`

---

## 2. 缺少必填参数

现象：

```text
缺少必填选项 '--table-name'
```

结论：

- 这是 CLI 参数绑定错误
- 退出码通常是 `2`

处理：

- 先跑 `DatabaseMcpServer tool help <tool_name>`
- 按帮助补齐参数名

---

## 3. `stdout` 不是纯 JSON

正常情况：

- 成功 tool 调用应只输出 JSON 到 `stdout`

如果混入额外文本：

- 优先怀疑底层库直接写 `Console.Out`
- CLI 层应尽量吞掉运行期 stray stdout/stderr

处理：

- 先记录具体命令和 stdout 内容
- 区分是 CLI 自己打印，还是数据库驱动/第三方库打印

---

## 4. SQL Server `Encrypt=True` 失败

现象示例：

```text
您试图连接的 SQL Server 实例要求加密，但是此计算机不支持
```

结论：

- 这是驱动 / TLS 能力问题
- 不是 tool 名称或参数错误

处理：

1. 原样报告错误
2. 不要静默修改连接串
3. 如果用户明确允许诊断，再尝试：

```text
Encrypt=False;TrustServerCertificate=True
```

说明：

- 这只是诊断 fallback
- 不是与 `Encrypt=True` 等价的安全设置

---

## 5. PowerShell JSON 转义错误

现象：

- `--commands` / `--queries-json` / `--parameters` 报 JSON 解析错误

处理：

- 用单引号包住整个 JSON
- 保证 JSON 本身是一个完整参数

正确示例：

```powershell
--parameters '{"age":18}'
--in-values '[1,2,3]'
--queries-json '{"users":"select * from users","roles":"select * from roles"}'
```

---

## 6. SQL Server `add_default_value` 字符串默认值

这个命令要传 SQL 字面量，不是裸文本。

错误示例：

```powershell
--default-value 'active'
```

正确示例：

```powershell
--default-value '''active'''
```

解释：

- SQL Server 需要收到 `'active'`
- PowerShell 单引号里要写成 `'''active'''`

---

## 7. 大规模 CLI 测试建议

如果要“测试所有 tool”：

- 先只读
- 再用唯一前缀创建隔离对象
- 每个工具记录：
  - 命令
  - 退出码
  - `stdout`
  - `stderr`
  - `success` / `errorMessage`
- 最后做清理

推荐前缀：

```text
cli_<yyyyMMdd_HHmmss>_<shortid>
```

---

## 8. 后端不支持 vs CLI 问题

要明确区分两类失败：

### CLI 问题

- 缺少参数
- 缺少 `--yes`
- JSON/PowerShell 转义错误
- `stderr` 出帮助文本

### 后端/数据库问题

- `success: false`
- 数据库对象不存在
- 存储过程/函数/触发器不支持
- 驱动能力不支持
- 权限不足

汇报时要把两类问题分开写。
