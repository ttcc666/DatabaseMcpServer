# DatabaseMcpServer NuGet Trusted Publishing 使用手册

本文档面向 `DatabaseMcpServer` 仓库维护者，说明如何通过 GitHub Actions 和 NuGet.org Trusted Publishing（受信任的发布）发布 NuGet 包。

Trusted Publishing 使用 GitHub OIDC token 换取短期 NuGet API key，不需要在 GitHub Secrets 中保存长期 `NUGET_API_KEY`。

---

## 1. 当前仓库配置

| 配置项 | 当前值 |
| --- | --- |
| GitHub 仓库所有者 | `ttcc666` |
| GitHub 仓库 | `DatabaseMcpServer` |
| NuGet Package ID | `DatabaseMcpServer` |
| NuGet.org 软件包所有者 | `ttcc` |
| GitHub Repository Variable | `NUGET_USER=ttcc` |
| GitHub Environment | `release` |
| Trusted Publishing workflow 文件 | `publish-nuget.yml` |
| Workflow 路径 | `.github/workflows/publish-nuget.yml` |
| NuGet 源 | `https://api.nuget.org/v3/index.json` |

实际 workflow：[`../.github/workflows/publish-nuget.yml`](../.github/workflows/publish-nuget.yml)。

> NuGet.org 策略中的 Environment 原始值必须是英文小写 `release`。如果浏览器自动翻译页面，界面可能显示成“发布”，应关闭翻译或进入编辑页面确认原始值。

---

## 2. 工作原理

```mermaid
flowchart LR
    A[发布 GitHub Release<br/>或手动运行 workflow] --> B[检出指定 Git tag]
    B --> C[验证 tag / main / PackageVersion]
    C --> D[Build + Test + Pack]
    D --> E[上传 workflow artifact]
    E --> F[GitHub 签发 OIDC token]
    F --> G[NuGet/login@v1]
    G --> H[NuGet.org 返回短期 API key]
    H --> I[dotnet nuget push]
```

关键安全属性：

- GitHub job 只申请 `contents: read` 和 `id-token: write`。
- `NuGet/login@v1` 在 build、test 和 pack 完成后才执行。
- NuGet.org 临时 API key 有效期为 1 小时。
- 每个 OIDC token 只能交换一个临时 API key。
- 仓库不保存长期 NuGet API key。
- `release` Environment 可以要求人工审批后才允许发布。

---

## 3. 一次性配置

这些设置通常只需要配置一次。仓库当前已经完成，但迁移仓库、修改所有者或重建策略时仍需核对。

### 3.1 创建 GitHub Environment

进入 GitHub 仓库：

```text
Settings → Environments → New environment
```

Environment 名称：

```text
release
```

推荐至少配置一项 protection rule：

- Required reviewers：发布前由维护者人工确认。
- Deployment branches and tags：限制允许进入该环境的分支或 tag。
- 不建议允许 Pull Request workflow 直接进入发布环境。

### 3.2 配置 NuGet 用户名

进入：

```text
Settings → Secrets and variables → Actions → Variables
```

添加 Repository variable：

```text
Name:  NUGET_USER
Value: ttcc
```

`NUGET_USER` 必须是 NuGet.org Profile username，不是邮箱地址。用户名本身不是凭据，因此当前使用 Repository variable，而不是长期 API key secret。

### 3.3 创建 NuGet.org Trusted Publishing Policy

登录 NuGet.org 后进入：

```text
用户名 → Trusted Publishing / 受信任的发布 → Add policy
```

填写：

| NuGet.org 字段 | 值 |
| --- | --- |
| Policy owner / 软件包所有者 | `ttcc` |
| Repository owner / 仓库所有者 | `ttcc666` |
| Repository / 存储库 | `DatabaseMcpServer` |
| Workflow file / 工作流程 | `publish-nuget.yml` |
| Environment / 环境 | `release` |

Workflow file 只能填写文件名：

```text
publish-nuget.yml
```

不要填写完整路径：

```text
.github/workflows/publish-nuget.yml
```

### 3.4 策略状态说明

- 策略显示 Active / 积极的，表示可以尝试 OIDC token exchange。
- 某些策略创建后只有 7 天临时激活窗口，应在期限内完成第一次成功发布。
- 如果 GitHub 仓库或组织所有权发生变化，策略可能变为非活动状态。
- NuGet policy 绑定的是软件包所有者，应谨慎控制 `release` Environment 的审批权限。

---

## 4. Workflow 的发布约束

当前 workflow 不会看到 tag 就直接上传，而是按顺序验证以下 Invariant（不变量）：

| 检查 | 失败时的意义 |
| --- | --- |
| Tag 符合 `3.5.6` 或 `3.5.6-preview.1` 格式 | 禁止不受支持的版本格式 |
| `refs/tags/<version>` 确实存在 | 禁止用普通 branch 名冒充版本 |
| Tag commit 等于检出的 HEAD | 防止构建错误 commit |
| Tag commit 位于默认分支 `main` 历史中 | 防止从未合并的开发分支发布 |
| 项目 `PackageVersion` 等于 tag | 防止 package 版本与 release 版本不一致 |
| `scripts/verify.ps1` 成功 | Build 或测试失败时禁止发布 |
| 预期 `.nupkg` 文件存在 | 防止推送错误路径或空产物 |

本仓库使用不带 `v` 前缀的 tag：

```text
3.5.6
```

不要使用：

```text
v3.5.6
```

---

## 5. 日常版本发布流程

以下以发布 `3.5.6` 为例。

### 5.1 同步开发分支

```powershell
git fetch 'origin'
git switch 'dev'
git pull --ff-only 'origin' 'dev'
git merge --ff-only 'origin/main'
```

如果 `--ff-only` 失败，说明分支历史已经分叉。不要强制推送，应先检查提交图并明确合并策略。

### 5.2 更新版本元数据

至少检查以下文件：

- `src/DatabaseMcpServer/DatabaseMcpServer.csproj` 中的 `PackageVersion`
- `.mcp/server.json` 中的包版本
- `mcp.json.example` 中的示例版本
- `README.md` / `README_EN.md` 的版本徽章和发布记录

项目文件应包含：

```xml
<PackageVersion>3.5.6</PackageVersion>
```

### 5.3 本地验证

```powershell
powershell -ExecutionPolicy 'Bypass' -File 'scripts\verify.ps1'

dotnet pack `
  'src\DatabaseMcpServer\DatabaseMcpServer.csproj' `
  '--configuration' 'Release' `
  '--output' 'artifacts\release'
```

检查包：

```powershell
Get-ChildItem 'artifacts\release\DatabaseMcpServer.3.5.6.nupkg'
Get-FileHash 'artifacts\release\DatabaseMcpServer.3.5.6.nupkg' -Algorithm 'SHA256'
```

### 5.4 提交并推送 dev

```powershell
git status --short
git add -- '.mcp/server.json' `
  'mcp.json.example' `
  'README.md' `
  'README_EN.md' `
  'src/DatabaseMcpServer/DatabaseMcpServer.csproj'
git commit -m 'chore: release 3.5.6'
git push 'origin' 'dev'
```

只暂存本次版本涉及的文件，不要在存在无关改动时使用 `git add -A`。

### 5.5 同步到 main

```powershell
git switch 'main'
git pull --ff-only 'origin' 'main'
git merge --ff-only 'dev'
git push 'origin' 'main'
```

### 5.6 创建并推送 tag

```powershell
git tag -a '3.5.6' -m 'Release 3.5.6'
git push 'origin' '3.5.6'
```

验证 tag：

```powershell
git show --no-patch --decorate '3.5.6'
git merge-base --is-ancestor '3.5.6' 'origin/main'
if ($LASTEXITCODE -ne 0) {
    throw 'Tag 3.5.6 不在 origin/main 历史中。'
}
```

### 5.7 发布 GitHub Release

进入：

```text
GitHub → Releases → Draft a new release
```

选择 tag `3.5.6`，填写 Release notes，然后点击 Publish release。

> Workflow 监听的是 `release.published`，仅推送 tag 不会自动上传 NuGet。必须正式发布 GitHub Release，或使用手动运行方式。

### 5.8 审批 release Environment

如果 `release` Environment 配置了 Required reviewers，workflow 会显示 Waiting。维护者检查以下信息后批准：

- Release tag 与计划版本一致。
- Tag commit 在 `main` 上。
- Release notes 正确。
- 没有重复或误发版本。

审批后 workflow 才会获得进入 Environment 的权限。

### 5.9 验证发布结果

检查 GitHub Actions：

```text
Actions → Publish NuGet → 对应版本运行记录
```

成功日志应包含：

- `Build and test` 成功
- `Pack NuGet package` 成功
- `Upload workflow artifact` 成功
- `Log in to NuGet.org with OIDC` 成功
- `Push package to NuGet.org` 成功

通过 NuGet flat container API 检查版本：

```powershell
$versions = (
  Invoke-RestMethod `
    -Uri 'https://api.nuget.org/v3-flatcontainer/databasemcpserver/index.json'
).versions

$versions | Select-String '3.5.6'
```

NuGet.org 页面：

```text
https://www.nuget.org/packages/DatabaseMcpServer/3.5.6
```

NuGet.org 可能需要几分钟完成索引，workflow 成功后页面暂时不可见不一定代表发布失败。

安装或升级验证：

```powershell
dotnet tool update --global 'DatabaseMcpServer' --version '3.5.6'
dotnet tool list --global | Select-String 'databasemcpserver'
DatabaseMcpServer tool list
```

---

## 6. 手动运行 Workflow

适用场景：

- Workflow 在 GitHub Release 发布之后才添加，旧 release 不会追溯触发。
- 自动运行失败，修复配置后需要重试。
- 想对指定的已有 tag 验证 OIDC 配置。

操作步骤：

1. 进入 `Actions → Publish NuGet`。
2. 点击 `Run workflow`。
3. Branch 必须选择 `main`。
4. Tag 输入 `3.5.6`。
5. 点击运行并审批 `release` Environment。

Workflow 明确限制手动运行必须从默认分支启动。从 `dev` 或其他 branch 启动时，publish job 会被跳过。

### 6.1 当前 3.5.5 的特殊情况

`3.5.5` GitHub Release 早于 Trusted Publishing workflow，因此不会自动重新触发。

- 如果 `3.5.5` 尚未上传 NuGet.org，可从 `main` 手动运行，tag 输入 `3.5.5`。
- 如果 `3.5.5` 已上传，NuGet 版本不可覆盖；workflow 使用 `--skip-duplicate`，不会替换已存在的包。
- 第一次完整的新版本发布建议使用下一个未发布版本，例如 `3.5.6`。

---

## 7. 常见问题排查

| 现象 | 常见原因 | 处理方式 |
| --- | --- | --- |
| Actions 中看不到 `Publish NuGet` | Workflow 尚未进入默认分支，或 YAML 无效 | 确认文件存在于 `main` 的 `.github/workflows/publish-nuget.yml` |
| 手动运行后 job 显示 Skipped | Run workflow 时选择了非默认分支 | 从 `main` 重新运行 |
| Workflow 一直 Waiting | `release` Environment 等待审批 | 由 Required reviewer 审核并批准 |
| `NUGET_USER` 为空 | Repository variable 未配置或名称错误 | 配置 `NUGET_USER=ttcc`，注意不是邮箱 |
| OIDC login 返回 401/403 | Trusted Publishing policy 字段不匹配 | 核对 owner、repo、workflow 文件名和 environment |
| Environment claim 不匹配 | NuGet.org 填写了中文“发布”或其他名称 | 将原始值改为英文 `release` |
| Policy 显示 inactive | 临时激活期已过或所有权变化 | 在 NuGet.org 重启策略或重新创建并尽快发布 |
| `Tag ... does not exist` | 输入了不存在的 tag | 先创建并推送对应 tag |
| Tag 不在默认分支 | Tag 指向未合并的 dev commit | 将变更合并到 main 后重新创建新版本 tag |
| `PackageVersion ... does not match` | `.csproj` 版本与 tag 不一致 | 修改 `PackageVersion`，重新提交并创建正确的新 tag |
| Build/test 失败 | 代码、依赖或测试问题 | 修复后使用同一未发布 tag 重跑；必要时创建新 tag |
| 包已存在 | NuGet 版本不可变 | 增加版本号；不能覆盖同版本 `.nupkg` |
| Workflow 成功但页面找不到版本 | NuGet.org 仍在索引 | 等待几分钟后用 flat container API 再检查 |
| OIDC login 成功但 push 较晚失败 | 临时 API key 超过 1 小时 | 保持 login 紧邻 push；当前 workflow 已按此设计 |

查看失败日志时，优先定位第一个失败步骤，不要只看最后的 job 结论。

---

## 8. 安全与回退

### 8.1 安全要求

- 不要创建或提交 `NUGET_API_KEY` 文件。
- 不要把长期 NuGet API key 写入 GitHub Repository Secrets 作为默认发布方式。
- 不要给发布 job 增加不必要的 `contents: write`、`packages: write` 或管理员权限。
- 不要给 workflow 增加 `pull_request` 发布触发器。
- 保留 `release` Environment 的人工审批规则。
- 定期检查 `NuGet/login@v1`、`actions/checkout`、`actions/setup-dotnet` 等 action 版本。
- 更严格的供应链环境可将 action tag 固定为审核过的 commit SHA。

### 8.2 Trusted Publishing 不等于包签名

Trusted Publishing 解决的是“谁有权上传包”的身份认证问题，不会自动为 `.nupkg` 添加 NuGet cryptographic signature。

如果需要显示为 Signed package，需要另外配置 NuGet package signing 和受信任证书。

### 8.3 发布失败时的处理顺序

1. 修复 GitHub Environment、Repository variable 或 NuGet policy。
2. 对同一个尚未成功发布的 tag 重新运行 workflow。
3. 如果版本已经存在，创建更高版本，不要尝试覆盖。
4. 只有在 Trusted Publishing 无法恢复且必须紧急发布时，才考虑 NuGet.org 网页手动上传。

紧急手动上传不应成为日常流程。不要把人工 API key 粘贴到 issue、commit、workflow 日志或聊天记录中；使用后应及时撤销。

### 8.4 暂停自动发布

需要临时停止时，优先在 GitHub Actions 页面 Disable workflow，或取消 `release` Environment 审批。

停用 workflow 或删除 Trusted Publishing policy 不会删除已经发布的 NuGet 包。

---

## 9. 发布检查清单

发布前：

- [ ] `PackageVersion` 已更新。
- [ ] README、MCP manifest 和示例版本已同步。
- [ ] `scripts/verify.ps1` 通过。
- [ ] Release commit 已进入 `main`。
- [ ] Tag 与 `PackageVersion` 完全一致。
- [ ] Tag 不带 `v` 前缀。
- [ ] NuGet.org 尚不存在该版本。

发布中：

- [ ] GitHub Release 已正式 Publish。
- [ ] `release` Environment 审批信息正确。
- [ ] OIDC login 成功。
- [ ] NuGet push 成功。

发布后：

- [ ] GitHub workflow artifact 可下载。
- [ ] NuGet.org 页面出现新版本。
- [ ] SHA256 已记录到 Release notes 或发布记录。
- [ ] 全局工具可以安装或升级。
- [ ] `DatabaseMcpServer tool list` 可以正常运行。

---

## 10. 参考资料

- [Microsoft Learn：可信发布 nuget.org](https://learn.microsoft.com/zh-cn/nuget/nuget-org/trusted-publishing)
- [NuGet/login GitHub Action](https://github.com/NuGet/login)
- [GitHub Actions：Publish NuGet](https://github.com/ttcc666/DatabaseMcpServer/actions/workflows/publish-nuget.yml)
- [DatabaseMcpServer NuGet 包](https://www.nuget.org/packages/DatabaseMcpServer)
