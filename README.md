# DingTalk 多开器 / DingTalk Multi-Open Manager

一个面向 Windows 桌面端钉钉的多账号管理与多开工具。它通过独立数据目录隔离不同账号，支持同时运行多个钉钉实例，并提供图形化账号管理界面。

> 本项目为非官方工具，与阿里巴巴集团及钉钉官方无关联。请遵守钉钉用户协议及所在组织的相关规范，使用前请自行评估风险。

## 功能

- 任意数量多开：每个账号使用独立的钉钉数据目录，实例数量不受程序限制。
- 账号管理：新建、重命名、删除账号，并可查看每个账号的运行状态。
- 独立数据：账号数据互不影响，可以在不同窗口中登录不同账号。
- 一键启动：在管理器中选择账号即可启动对应的钉钉实例。
- 自动恢复：启动完成后自动还原默认数据目录，不影响日常使用主账号。

## 系统要求

- Windows 10 / 11 x64
- 钉钉 PC 正式版
- 已在钉钉 `8.5.0-Release.260817002` 上完成三实例并存验证
- 默认安装路径：`D:\DingDing\DingtalkLauncher.exe`

程序会优先从注册表读取钉钉的安装位置；找不到时会尝试上述默认路径。

## 使用方法

1. 从 [Releases](../../releases) 下载最新的发布包并解压。
2. 双击 `DingTalk多开器.exe`。
3. 在账号管理器中点击“新建账号”，再点击“打开选中账号”。
4. 新账号第一次启动时会显示登录二维码，使用对应钉钉账号扫码登录。
5. 重复以上操作可以继续创建更多账号。

如果提示权限不足，请右键程序并选择“以管理员身份运行”。

## 数据位置

- 账号管理列表：`%APPDATA%\DingTalkMultiOpen\accounts.txt`
- 主账号数据：`%APPDATA%\DingTalk`
- 其他账号数据：`%APPDATA%\DingTalk2`、`%APPDATA%\DingTalk3` 等

删除管理器中的账号只会删除列表项，不会删除对应数据目录。

## 工作原理

钉钉桌面端通过命名互斥量限制重复实例，同时多个实例也不能共用同一份用户数据。本工具采用以下方式实现多开：

1. 找到并关闭钉钉当前持有的单实例互斥量句柄，不结束已有钉钉进程。
2. 通过 `%APPDATA%\DingTalk\redirectAppData.dat` 将新实例的数据目录临时重定向到独立目录。
3. 等待新实例稳定运行后删除重定向文件，恢复正常启动行为。

核心使用的互斥量名称：

```text
{{239B7D43-86D5-4E5C-ADE6-CEC42155B475}}DingTalk
{{239B7D43-86D5-4E5C-ADE6-CEC42155B475}}DingTalk_loginframe
```

## 从源码编译

项目使用 Windows 自带的 .NET Framework 编译器，不需要额外安装 SDK。

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe ^
  /nologo /platform:x64 /codepage:65001 /target:winexe ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /out:DingTalk多开器.exe DingTalkMultiCore.cs DingTalk多开器.cs
```

## 已知限制

- 钉钉升级后如果更换单实例互斥量的 GUID，需要同步修改源码中的 `MutexGuidPart`。
- 若程序异常退出并残留 `redirectAppData.dat`，请手动删除 `%APPDATA%\DingTalk\redirectAppData.dat` 后再正常启动钉钉。
- 跨进程关闭句柄的行为可能被部分安全软件提示，这是此类工具的常见现象。
- 本项目不会上传或收集任何账号信息；登录和聊天数据均由钉钉本地客户端管理。

## 免责声明

本项目仅供个人学习和研究使用。使用者应自行确认使用场景符合钉钉用户协议以及所在单位的管理规定。因使用本工具导致的账号风险、数据问题或其他损失，由使用者自行承担。

## 许可证

发布前请根据你的意愿为项目选择许可证。若希望允许他人自由使用、修改和分发，推荐使用 [MIT License](https://choosealicense.com/licenses/mit/)。
