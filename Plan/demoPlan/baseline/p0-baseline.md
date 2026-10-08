# P0 开发基线记录

记录日期：2026-09-21  
工程路径：`C:\Users\qqcom\Documents\ChatGPT\数字人\camel-digitalman-main`  
基线性质：只读盘点；不撤销、不覆盖基线时已经存在的工作树改动。

## 工程环境

- Unity：2022.3.62f3（revision `96770f904ca7`）
- 渲染管线：URP 14.0.12
- 已登记构建场景：`Assets/Scenes/Begin.unity`、`Assets/Scenes/main.unity`
- 工厂场景：尚未创建
- P0/P1 工厂模块目录：`Plan/demoPlan/`（后续 P2 起使用 `Assets/FactoryLab/`）

## 已有数字人能力

已确认工程已有：

- `VoiceChatController.SubmitTextMessage(string)` 文字消息入口。
- Space 推送语音输入、讯飞 ASR、DeepSeek 对话和多种 TTS Provider。
- `onAssistantDelta`、`onError`、`onConversationChanged`、`onKnowledgeBaseChanged` 等 UnityEvent。
- SALSA/连续口型播放和现有人物模型切换。
- `KnowledgeBaseStore` 扫描并读取 `.txt`、`.json` 文件，构造初始知识上下文。
- `Application.persistentDataPath/VoiceChat/KnowledgeBase/` 运行时知识库目录及运行时刷新设置。
- `BuildRuntimeSystemContext` 可作为后续注入 FactoryLab 实验状态的接入点。

## 现有场景与入口

- `Begin.unity`：入口界面，`SceneFadeManager.OnClickStart()` 加载 `main`。
- `main.unity`：当前数字人主交互场景，包含语音、设置、记忆、知识库和角色切换。
- 新工厂模块计划使用独立 `FactoryLab` 场景；P0/P1 不修改构建场景列表，也不修改 `main.unity`。

## 当前工作树状态

采集时工作树已经存在用户或此前工作产生的改动。当前 `git status --short` 显示 77 行状态，主要包括：

- 已修改：`.gitignore`、`Assets/Scenes/main.unity`、`Assets/Scripts/VoiceChatController.cs`、配置和 TTS/UI 相关脚本、`README.md`。
- 未跟踪：Azure TTS、连续口型、Assistant UI 验证脚本及 `Docs/` 等文件。
- `git diff --stat`：14 个已跟踪文件，约 9,364 行新增、2,612 行删除。

这些改动不由 P0/P1 处理，不执行 reset、checkout、clean 或自动提交。后续工厂工作应尽量避开上述文件，必要时先单独确认。

## 教材来源基线

- 文件：`C:\Users\qqcom\Documents\ChatGPT\数字人\output\pdf\基础工业工程_第四章_第50-101页.pdf`
- SHA-256：`281DE15B242076515ED658665CDB6FCEF3BE439D8057F8037B3080F0FB14EDE0`
- 文件为第四章相关扫描页，运行时不直接加载 PDF；知识包使用人工整理后的 TXT/JSON。
- 书内重点页码：第 41～45 页程序分析概述及方法；第 45 页起工艺程序分析；第 55 页起流程程序分析；第 73 页起布置和经路分析。

## 模块边界和 P0 验收

P0 只完成基线和边界确认，不新增 Unity 场景、不改变数字人运行逻辑、不写入运行时密钥。

后续 FactoryLab 的公共接口预留为：

1. `FactoryExperimentSnapshot`：当前实验状态快照。
2. `FactoryEventRecord`：产品活动事件。
3. `FactoryExperimentResult`：一轮实验结果。
4. `FactoryTeachingContextProvider`：向数字人上下文提供教学快照。

P0 通过条件：工程版本、现有能力、知识库接入点、构建场景、脏工作树风险和教材来源均有记录；P1 文件只新增在 `Plan/demoPlan/` 下。

