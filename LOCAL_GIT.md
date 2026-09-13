# 本地 Git 使用说明

仓库只管理本 Unity 项目，没有配置远程仓库。`main` 保存移除面部识别前的基线，`remove-face-emotion` 保存删除后的版本。

在项目目录打开终端，日常操作：

```powershell
git status
git diff
git add Assets Packages ProjectSettings
git diff --cached --stat
git commit -m "描述本次修改"
git log --oneline --decorate -5
```

新增根目录文档或 Git 配置文件时，单独执行 `git add 文件名`。提交前检查暂存内容，勿提交密钥。运行配置保存在 Unity 的 persistentDataPath 下，不在仓库中。

切换版本前退出 Unity，并提交或妥善保存当前改动。`git switch main` 查看删除前版本，`git switch remove-face-emotion` 回到删除后版本。切换后 Unity 可能重新导入资源。

Assets 的 `.meta` 必须与资源一起管理。二进制贴图、模型、音视频等由 Git LFS 管理，Unity 文本场景、预制体和文本 mesh 使用普通 Git。Library、Temp、Logs、UserSettings 和构建目录不提交。

文件备份位于项目外的 `../garbage/`。本地 Git 不提供异地备份；不要删除项目内 `.git`，它包含版本历史及本地 LFS 对象。

## 本次验证

- Unity 2022.3.62f3 批处理重新编译成功，Barracuda 已从包清单及锁文件移除。
- 主场景与启动场景加载成功；没有新增未解析的脚本 GUID。
- 主场景中面部识别对象已删除；旧聊天表情附加内容过滤和文本语气提示构造通过自动检查。
- 主场景仍报告 3 个 Missing Script，属于已有资源问题。
- 真实麦克风、外部聊天与语音服务、口型及画面效果仍需在 Unity 播放模式下手动验收。


新增功能位于 `feature/text-chat-model-profiles` 分支，文字聊天和多套接口配置见 [CHAT_MODELS.md](CHAT_MODELS.md)。
