# 文字聊天与模型配置

## 打字聊天

在 Unity 播放模式打开主场景，展开右侧“记忆与知识库”面板。输入消息后按 Enter 或点击“发送”；Shift+Enter 换行。中文输入法选词时回车不会发送。输入框获得焦点时，空格不会启动录音。

默认同时显示文字并朗读。“朗读回复”可切换为纯文字，设置保存在本机；语音服务不可用时仍会继续文字回答。新的消息会打断上一轮生成和朗读；“停止”只停止当前回答，保留已生成的内容。切换模型继续沿用当前聊天，独立聊天请点击“新对话”。

## 添加模型

1. 点击左下角“设置”，进入“大模型”。
2. 点击“+ DeepSeek”“+ 火山方舟”或“+ 兼容接口”。
3. 填写模型名称、URI、API 密钥。模型名称必须是平台实际提供的名称；火山方舟也可填写平台提供的推理接入点 ID。配置名称可不填。
4. 可点击“测试连接”，它只发送独立的短测试消息，不发送聊天记录和知识库，也不朗读；会产生该平台正常的 API 请求费用。
5. 点击“保存并启用”。左侧列表用于选择要编辑的配置，选中本身不会替换正在使用的接口。

URI 可以是基础地址，也可以是完整接口地址：

| 服务 | 基础 URI 示例 |
| --- | --- |
| DeepSeek | `https://api.deepseek.com` |
| 火山方舟 | `https://ark.cn-beijing.volces.com/api/v3` |
| OpenAI | `https://api.openai.com/v1` |
| 自建兼容服务 | 使用服务提供方给出的基础地址 |

基础 URI 自动追加 `/chat/completions`，已经包含该后缀时不会重复追加，也不会自动猜测 `/v1`。页面显示最终请求地址。

支持 Bearer 密钥认证的 OpenAI Chat Completions 兼容接口，不包含 Responses、Realtime 或其他专用签名协议。模型权限、平台额度和接口支持的参数仍以服务提供方为准。

## 高级选项

在“大模型”页面向下滚动可查看工具调用、温度、Top P、输出长度、DeepSeek 思考开关、历史长度及系统提示词。

新配置默认仅发送基本聊天字段，避免专用参数导致接口拒绝请求。工具调用默认关闭，需要模型支持并在此启用，同时保留项目“功能”页面中的工具开关。工具请求失败时回退普通聊天。`max_completion_tokens` 供要求该字段的模型使用；不确定时先不启用输出长度参数。

系统提示词和历史长度在配置间共用。豆包语音的语气分析也使用当前对话模型，无需另配 DeepSeek。

## 配置与版本

旧 DeepSeek 配置自动迁移为第一套模型配置，旧密钥、参数、聊天记录和知识库保留。运行配置仍保存在 Unity `persistentDataPath/RuntimeConfig/voice-chat-settings.json`，不纳入 Git。请勿将包含密钥的运行配置复制进 Assets。

当前功能分支为 `feature/text-chat-model-profiles`。修改前文件及原运行配置备份位于项目外 `../garbage/text-chat-models-20260913-003228/`，由项目所有者手动清理。

## 验证

Unity 编辑器验证脚本为 `Assets/Editor/ChatFeatureValidation.cs`：`Run` 使用本地模拟服务和隔离配置，`RunLiveConfigured` 使用当前本机模型配置发送一条不带历史的短消息。模拟服务启动脚本为 `Tools/chat_mock_server.py`。先在项目目录运行 `python Tools/chat_mock_server.py`，再用 Unity 的 `-executeMethod ChatFeatureValidation.Run` 启动验证；服务只监听本机 18769 端口。

已验证配置迁移、URI、请求字段、流式与普通 JSON 回复、取消、模型切换保留会话、TTS 失败后的文字回复、场景控件与密钥遮挡。现有 DeepSeek 接口已通过真实短请求测试。火山方舟及其他兼容服务未提供凭据，尚未实连验证。

中文输入法操作、实际麦克风、真实语音播放与口型仍需在播放模式下人工验收。场景原有 Missing Script 问题不属于本次功能修改。
