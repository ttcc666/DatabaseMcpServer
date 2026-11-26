# Doris 数据库配置指南

依据 [SqlSugar 官方文档](https://www.donet5.com/Doc/1/2577)。Apache Doris 使用 MySQL 协议连接（FE 默认查询端口 `9030`），在 SqlSugar 内部归入 MySQL 兼容家族。当前完成离线构造验证，尚未进行 Doris 实库验收。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "doris-olap",
      "connectionString": "Server=localhost;Port=9030;Database=mydb;Uid=root;Pwd=123456;Pooling=false;",
      "dbType": "Doris",
      "description": "Doris OLAP（FE 默认端口 9030；建议 Pooling=false）",
      "isDefault": false,
      "accessMode": "read-only",
      "optimizationSettings": {
        "disableNvarchar": "false"
      }
    }
  ]
}
```

---

## 🔧 连接与能力说明

- **端口与连接池**：Doris FE MySQL 协议端口通常为 `9030`。如遇到连续请求报错 `Unsupported command`，请在连接字符串中显式配置 `Pooling=false`。
- **ORM 设置**：`disableNvarchar` 映射到 `ConnMoreSettings.DisableNvarchar`（默认 `false`）；`disablePooling` / `maxPoolSize` 保留为 `advisory`，需通过原生连接字符串配置。
- **能力边界**：通用 SQL 查询与基础表/列读取按驱动执行；OLTP 存储过程、函数、触发器、约束变更、注释与专用键集分页等能力不可用。详见 [ORM 配置约定](../Doc/orm-configuration.md)。
