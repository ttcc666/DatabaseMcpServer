# .NET操作OceanBase

## OceanBase介绍

OceanBase数据有2种模式MySql和Oracle

## 1、 OceanBase 【MySql模式 】

### 1.1 Nuget安装

```
SqlSugarCore
```

### 1.2 DbType设置成MySql

```csharp
SqlSugarClient db = new SqlSugarClient(new ConnectionConfig()
{
    DbType = DbType.OceanBase,
    ConnectionString = "server=localhost;Database=SqlSugar4xTest;Uid=root;Pwd=haosql;",
    IsAutoCloseConnection = true,
    //个别特殊的数据库需要禁用Nvarchar
    MoreSettings = new ConnMoreSettings() { DisableNarvchar = true }
});
//注意：
//特殊服务器连续执行2次写入操作报错说明服务器不支持连接池
//字符口上加 Pooling=false可以解决;
```

### 1.3 Hints配置

Optimizer Hints 可以用在SQL语句中改变执行计划，懂这个的用 , 不懂的就先不要看了

```csharp
db.Queryable<Order>().Hints("/*+ ... */").ToList();
```

## 2、OceanBase【Oracle模式】

### 2.1 Nuget安装

```
SqlSugar.OceanBaseForOracleCore //需要升级到5.1.4.92-preview14+ SqlSugarCore
```

### 2.2 安装odbc驱动

```
//安装odbc驱动 ob-connector-odbc-2.0.8.2-win64.msi
```

### 2.3 创建db对象

字符串

```text
Driver={OceanBase ODBC 2.0 Driver};Server=172.19.9.9;
Port=2883;Database=XIR_TRD;User=XIR_TRD@Xpia2C6G#obtest:1650773680;
Password=aaAA11%%;Option=3;
```

```csharp
//程序启动时加上只要执行一次 InstanceFactory.CustomAssemblies = new System.Reflection.Assembly[] { typeof(OceanBaseForOracleProvider).Assembly }; 
//创建db
SqlSugarClient db = new SqlSugarClient(new ConnectionConfig()
{
    DbType = DbType.OceanBaseForOracle,
    ConnectionString = "...",
    IsAutoCloseConnection = true,
    //个别特殊的数据库需要禁用Nvarchar
    //MoreSettings=new ConnMoreSettings() { DisableNarvchar=true }
});
//需要升级到5.1.4.92-preview+
```

### 2.4 Oracle自增用法

建议用雪花ID作主键

https://www.donet5.com/Home/Doc?typeId=2561

如果非要用的话并发有些不友好，并发高可能需要自已加锁返回ID

看Oracle文档，自增用法一样

https://www.donet5.com/Home/Doc?typeId=1220

### 2.5、varchar2字段问题

odbc无法配置DbType为varchar2建议数据库字段用varchar
