# Microsoft Access 配置指南

Access 走默认优化策略，无额外选项，适用于轻量桌面数据文件。

---

## 📋 配置方式

### 配置文件 (databases.json)

```json
{
  "databases": [
    {
      "name": "access-main",
      "connectionString": "Data Source=D:\\data\\demo.accdb;",
      "dbType": "Access",
      "description": "Access 数据库",
      "isDefault": false
    }
  ]
}
```

> 请确保机器已安装 Access Database Engine / OLEDB 提供程序。

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

- 使用 `DefaultOptimizationStrategy`（无额外优化项）。

---

## 📦 依赖要求

- NuGet: `SqlSugarCore`（已默认）
- 系统需安装 Access 数据库引擎。

---

## 🔗 参考资料

- [Access（SqlSugar）](https://www.donet5.com/Doc/1/2410)

---

**最后更新**: 2025-12-08
