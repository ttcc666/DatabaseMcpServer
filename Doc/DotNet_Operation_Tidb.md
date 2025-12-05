# .NET操作 Tidb

## 1、 Tidb

### 1.1 Nuget安装

```
SqlSugarCore
```

### 1.2 DbType设置成MySql

```csharp
SqlSugarClient db = new SqlSugarClient(new ConnectionConfig()
{
    DbType = DbType.Tidb,
    ConnectionString = "server=localhost;Database=SqlSugar4xTest;Uid=root;Pwd=haosql;",
    IsAutoCloseConnection = true,
    //个别特殊的数据库需要禁用Nvarchar
    MoreSettings = new ConnMoreSettings() { DisableNarvchar = true }
});
```

### 1.3 Hints配置

Optimizer Hints 可以用在SQL语句中改变执行计划，懂这个的用，不懂的就先不要看了

```csharp
db.Queryable<Order>().Hints("/*+ ... */").ToList();
```

## 2、示例

查询

```csharp
db.Queryable<Student>().ToList(); //查询所有

db.Queryable<Student>().Where(it => it.Id == 1).ToList(); //根据条件查询

//分页  int pageIndex = 1; // pageindex是从1开始的不是从零开始的
int pageSize = 20;
int totalCount = 0;
//单表分页
var page = db.Queryable<Student>().ToPageList(pageIndex, pageSize, ref totalCount);
```

插入

```csharp
//返回插入行数
db.Insertable(insertObj).ExecuteCommand(); //都是参数化实现

//插入返回自增列
db.Insertable(insertObj).ExecuteReturnIdentity();

//返回雪花ID 看文档3.1具体用法（在最底部）
long id = db.Insertable(实体).ExecuteReturnSnowflakeId();
```

更多用法看左边菜单
