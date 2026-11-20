# 人大金仓数据库配置

依据 [SqlSugar 官方文档](https://www.donet5.com/Doc/1/2368)。使用 SqlSugar Kdbndp provider；配置别名 `kingbase` 保留。当前完成离线驱动构造验证，尚未进行金仓实库验收。

## 可运行配置格式

```json
{
  "databases": [
    {
      "name": "kingbase-main",
      "dbType": "Kdbndp",
      "connectionString": "Server=localhost;Port=54321;Database=app;User ID=SYSTEM;Password=replace-me;",
      "isDefault": true,
      "accessMode": "read-only",
      "optimizationSettings": {
        "mode": "Oracle",
        "camelCase": false,
        "disableNvarchar": false
      }
    }
  ]
}
```

用户名键使用 `User ID`。当前 Kdbndp 驱动不接受预设旧写法 `User=...`。布尔和数字优化项可以直接写 JSON 标量，保存时统一为字符串。

## ORM 设置

| 选项 | 允许值 | 实际作用 |
| --- | --- | --- |
| mode | Oracle / MySql / PostgreSQL / SqlServer | ConnMoreSettings.DatabaseModel；必须匹配服务端兼容模式 |
| camelCase | true / false | 控制 IsAutoToUpper，不主动把名称改为驼峰 |
| disableNvarchar | true / false | ORM 参数生成设置 |

默认 mode=Oracle，并默认标识符转大写。MySQL/PostgreSQL 模式的 nvarchar 设置由模式初始化，显式 disableNvarchar 可覆盖。大小写选项影响 ORM 生成行为，不重写用户 SQL。

| 旧优化键 | 当前状态 |
| --- | --- |
| schema / maxPoolSize | advisory；需在驱动连接字符串配置 |
| enableCursor / enableJson / enableGeometry / enableArray | not-applicable；当前 MCP 未实现这些专用参数开关 |
| enableBulkCopy / batchSize | not-applicable；批命令工具不是 BulkCopy |

旧键保留但不报告“已启用”。未知键保留并显示 unknown；已知键的非法值会被拒绝。maxPoolSize 等正整数配置不接受负数或零。

## 连接与能力边界

连接池、TLS、Search Path、认证由原生连接串负责。向导覆盖基础字段，更多选项使用原始模式并对照当前 Kdbndp 驱动版本验证。配置校验只做本地解析，不连接数据库；成功不代表服务端模式、权限或认证正确。

官方游标示例需要连接串 `CursorAsDataRead=True` 及 `SugarParameter.IsRefCursor=true`。当前 MCP 参数模型未接入 IsRefCursor，因此设置 enableCursor 不能开启游标。JSON IN 数组也不等于数据库原生数组。

当前通用 SQL 查询按 provider 执行，需实库验证；专用表分页、多结果集及类型化过程输出返回 unsupported。同步元数据、DDL/过程执行遵守服务端超时上限，尚不支持请求取消。不要用 Task.Run 包装同步调用推断服务端命令被取消。

先检查 `config validate`，再使用 `config doctor --test-connections` 验证真实连接；`get_database_capabilities` 返回有效 ORM 设置及适用范围。详见 [统一 ORM 配置约定](../Doc/orm-configuration.md)。
