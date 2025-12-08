# 自定义数据库配置指南

适用于 SqlSugar 未内置映射的数据库，需自行提供驱动与连接串。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "custom-main",
      "connectionString": "your-connection-string",
      "dbType": "Custom",
      "description": "自定义数据库",
      "isDefault": false
    }
  ]
}
```

### MCP 配置

```json
{
  "mcpServers": {
    "database": {
      "command": "DatabaseMcpServer",
      "env": {
        "DB_CONFIG_PATH": "D:\\config\\databases.json"
      }
    }
  }
}
```

---

## 🚀 自动性能优化

- 使用 `DefaultOptimizationStrategy`，无额外优化项；若需要特定能力，请扩展策略后再配置。

---

## 📦 依赖要求

- NuGet: `SqlSugarCore`（已默认）
- 自行安装/引用目标数据库驱动。

---

## 🔗 参考资料

- [扩展数据库（SqlSugar）](https://www.donet5.com/Doc/1/2411)

---

**最后更新**: 2025-12-08
