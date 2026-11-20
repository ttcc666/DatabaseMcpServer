# 华为 GaussDB / OpenGauss 配置

依据 [SqlSugar 官方文档](https://www.donet5.com/Doc/1/2438)。本项目按操作独占使用 SqlSugarClient，连接复用由底层驱动池负责。当前只验证了驱动离线构造，尚未做 GaussDB/OpenGauss 实库验收。

## 驱动与数据库模式

| dbType | 实际驱动 | 数据库模式 |
| --- | --- | --- |
| GaussDB | Npgsql | GaussDB 兼容路径 |
| OpenGauss | Npgsql | OpenGauss 兼容路径 |
| GaussDBNative | OpenGauss.NET | optimizationSettings.databaseModel 选择 GaussDB 或 OpenGauss |

驱动由 `dbType` 决定。旧键 `nativeDriver` 保留为配置建议，不切换驱动、不修改 DatabaseModel。原生 provider 默认模式为 GaussDB；OpenGauss 原生连接必须显式指定 OpenGauss。旧键 `isOpenGauss=true/false` 仍可选择原生模式；与显式 databaseModel 冲突时拒绝配置。

Npgsql 示例：

```json
{
  "databases": [
    {
      "name": "gauss-npgsql",
      "dbType": "GaussDB",
      "connectionString": "Host=localhost;Port=5432;Database=app;User ID=app;Password=replace-me;No Reset On Close=true;Pooling=true;Maximum Pool Size=50;",
      "isDefault": true,
      "accessMode": "read-only"
    }
  ]
}
```

OpenGauss 原生示例：

```json
{
  "databases": [
    {
      "name": "opengauss-native",
      "dbType": "GaussDBNative",
      "connectionString": "Host=localhost;Port=5432;Database=app;User ID=app;Password=replace-me;",
      "isDefault": true,
      "accessMode": "read-only",
      "optimizationSettings": {
        "databaseModel": "OpenGauss"
      }
    }
  ]
}
```

Npgsql 连接 OpenGauss 时改用 `dbType=OpenGauss`。按官方文档核对服务端认证配置；不要把 Npgsql 的连接参数原样移植到原生驱动。

## 配置生效范围

| 选项 | 实际行为 |
| --- | --- |
| databaseModel / isOpenGauss | 仅 GaussDBNative：设置 ConnMoreSettings.DatabaseModel |
| nativeDriver | 旧配置保留，advisory；驱动只由 dbType 选择 |
| schema / maxPoolSize / noResetOnClose | advisory，不修改连接字符串 |
| typeMapping / batchSize | not-applicable，没有对应 MCP 执行实现 |

optimizationSettings 支持字符串、布尔值及数字标量；保存时统一为字符串。未知键保留并标为 unknown。Gauss 系策略默认 DisableNvarchar=true，该设置不会重写原始 SQL。

Schema 使用驱动支持的 Search Path，或在 SQL 中显式限定对象。连接池、TLS、认证和会话选项放在原生连接字符串。Npgsql 的 No Reset On Close 按官方对应模式配置；它保留会话状态，不应被泛化为所有驱动的性能开关。

## MCP 能力边界

- Npgsql 路径的表分页/多结果集可用，但产品验证状态为 unverified；不能把 PostgreSQL 测试当作 GaussDB 验证。
- GaussDBNative 的专用分页/多结果集当前返回 unsupported。通用 SQL 读取需要按服务端验证。
- 未提供原生数组、游标输出、BulkCopy 或自动特殊类型映射工具。
- 批命令工具顺序执行命令，不因 batchSize 启用批量导入，也不保证事务原子性。
- ADO 超时以 MCP 执行策略为准；连接字符串 Command Timeout 不等于 SqlSugar.Ado.CommandTimeOut。
- 同步元数据、DDL、过程和 GO 支持超时上限，但未实现请求取消。

先执行 `config validate` 检查离线选项，再通过 `config doctor --test-connections` 或连接测试工具检查真实网络与认证。使用 `get_database_capabilities` 查看产品、实际 provider、选项和验证范围。向导只覆盖预设字段，TLS/Search Path 等高级选项使用原始连接串模式。

详见 [统一 ORM 配置约定](../Doc/orm-configuration.md)。
