# customDigitalman

口型新增 Azure 连续动画路径：按小句获取音频和面部动画，使用播放时间轴驱动 test / girl 的嘴部，并保留 Viseme / SALSA 降级。使用方法、模型映射和同音频 A/B 校准见 [连续口型说明](Docs/ContinuousLipSync.md)。

Unity 数字人语音交互项目。当前版本以 `main` 场景为核心，把 3D 数字人、360 环境、语音识别、DeepSeek 对话、四种 TTS、SALSA 口型同步、摄像头情绪识别、会话记忆、本地知识库和百度千帆联网搜索整合在一起。

## 项目环境

- Unity：`2022.3.62f3`
- 渲染管线：URP `14.0.12`
- 构建场景：`Assets/Scenes/Begin.unity`、`Assets/Scenes/main.unity`
- 主要包和插件：
  - `com.soupday.cc3_unity_tools`，本地路径 `E:/Unity/Addons/CCiC-Unity-Tools-2.2.6`
  - `com.unity.barracuda`，MoodMe 情绪识别依赖
  - `com.unity.nuget.newtonsoft-json`，由包依赖引入，运行时实际用于 DeepSeek、知识库、搜索等 JSON 处理
  - `Assets/Plugins/SALSA LipSync Suite2.5.4`
  - `Assets/Plugins/MoodMeBarracuda`
  - `Assets/Plugins/Reallusion/CCiC Unity Tools`
  - `Assets/Plugins/Eight Realm Horizons - 360 Skybox Pack`

如果 Package Manager 无法解析 `com.soupday.cc3_unity_tools`，先确认本机是否有上面的绝对路径，或在 `Packages/manifest.json` 中改成自己的本地安装路径。

## 场景结构

### `Begin.unity`

入口场景，包含开始界面、淡入淡出切场景、天空盒自动循环和一个可点击角色。

- `SceneFadeManager` 挂在 `black` 对象上，`OnClickStart()` 会加载 `main` 场景。
- `SkyboxCubemapCycle` 挂在 `SkyboxManager`，使用 `Custom/SkyboxCubemapBlend` shader 做旋转和交叉淡入淡出。
- `ClickCharacter` 挂在角色上，点击角色会随机触发 `Wave`、`Angry`、`Bow` 动画。
- `VRCamera` 在入口场景也有挂载，但该组件当前序列化为禁用状态。

### `main.unity`

主交互场景。关键对象：

- `VoiceChatSystem`：挂载 `AudioSource`、`StreamingPcmAudioPlayer`、`VoiceChatController`。
- `AdvancedSettingsCanvas`：运行时高级设置面板，包含 API Key、ASR、TTS、DeepSeek、百度搜索、功能开关、记忆、知识库和工具设置。
- `VoiceChatMemoryPanel`：显示当前会话、知识库状态、数据目录，并支持加载、刷新和删除历史会话。
- `EmotionHUDPanel`：MoodMe 摄像头预览、人脸框和七类情绪条。
- `SwitchModelButton`：通过 `CharacterModelSwitcher` 在 `test` 和 `girl` 两个模型之间切换。
- `Exit`：退出按钮。
- `定位按钮`：通过 `CharacterCameraPositioner` 把当前角色放到摄像机前方。
- 主场景还挂了 Eight Realm Horizons 的 `SkyboxSwitcher`，用于按钮切换 360 天空盒材质。

## 主运行链路

```text
按住 Space
 -> Unity Microphone 采集 16-bit PCM
 -> XFASRService 通过讯飞中文 ASR WebSocket 识别
 -> 松开 Space 后得到最终文本
 -> VoiceChatController 汇总用户文本、会话记忆、知识库、情绪快照和工具上下文
 -> DeepSeekService 调用 DeepSeek Chat Completions
 -> 可选：DeepSeek 先调用 get_current_datetime / web_search 工具
 -> DeepSeek 流式输出文本
 -> 当前 TTS Provider 分段合成语音
 -> StreamingPcmAudioPlayer 把 raw PCM 写入非 streamed AudioClip
 -> VoiceChatSystem.AudioSource 播放
 -> SALSA 读取同一个 AudioSource 驱动口型
 -> 角色 Animator 的 isTalk / talkAnimationIndex 驱动说话动作
```

`main.unity` 里 `VoiceChatController.ttsProvider` 当前序列化默认值是 `XunfeiOnline`。注意：`Awake()` 会先读取 `Application.persistentDataPath/RuntimeConfig/voice-chat-settings.json`，如果该文件存在，运行时配置会覆盖场景里的序列化值。

## 输入与交互

- 默认按住 `Space` 开始说话，松开 `Space` 结束录音并发送给 DeepSeek。
- 新一轮说话会调用 `InterruptAssistantOutput()`，取消当前 DeepSeek/TTS、清空播放流，并关闭说话动画。
- 设置面板可以修改 `pushToTalkKey`、是否启用按键说话、是否启动即监听、是否把情绪写入 prompt 等功能开关。
- 角色切换会同步当前活动模型、重绑说话 Animator，并用反射把两个模型上的 SALSA `audioSrc` 指向同一个 TTS `AudioSource`。

## 配置资产

已有配置资产位于：

```text
Assets/MyCharacter/Config/
  AzureTTSConfig.asset
  BaiduSearchConfig.asset
  DeepSeekConfig.asset
  DoubaoTTSConfig.asset
  XFASRConfig.asset
  XFOnlineTTSConfig.asset
  XFTTSConfig.asset
```

也可以在 Unity Project 窗口右键创建：

```text
Create > Voice Chat > Configs > XF ASR Config
Create > Voice Chat > Configs > XF TTS Config
Create > Voice Chat > Configs > XF Online TTS Config
Create > Voice Chat > Configs > Azure TTS Config
Create > Voice Chat > Configs > Doubao TTS Config
Create > Voice Chat > Configs > DeepSeek Config
Create > Voice Chat > Configs > Baidu Search Config
```

不要提交真实 API Key。建议通过运行时高级设置面板写入本机 `Application.persistentDataPath/RuntimeConfig/voice-chat-settings.json`，或使用本地私有配置。

## 外部服务

### 讯飞 ASR

配置类：`XFASRConfig`

默认端点：

```text
wss://iat.xf-yun.com/v1
```

当前识别参数默认值包括：

```text
domain = slm
language = zh_cn
accent = mandarin
dynamicCorrection = wpgs
sampleRate = 16000
frameBytes = 1280
frameIntervalMs = 40
maxRecordSeconds = 55
```

### DeepSeek

配置类：`DeepSeekConfig`

默认端点和模型：

```text
https://api.deepseek.com/chat/completions
model = deepseek-v4-flash
stream = true
thinking.type = disabled
```

`DeepSeekService` 支持两种调用：最终回答用 SSE 流式返回；工具决策和豆包语音指令分析用普通非流式请求。

### TTS Provider

`VoiceChatController` 当前支持四种 TTS：

```text
XunfeiSuperSmart = 讯飞超拟人 TTS
XunfeiOnline     = 讯飞在线 TTS
Doubao           = 豆包语音合成模型 2.0
AzureSpeech      = 微软 Azure Speech（全球版）
```

#### 讯飞超拟人 TTS

配置类：`XFTTSConfig`

- 默认端点形态：`wss://cbm01.cn-huabei-1.xf-yun.com/v1/private/...`
- 当前播放器要求 `audioEncoding = raw`
- 默认 `sampleRate = 24000`
- DeepSeek 的流式文本会按标点或长度分段发给同一个 WebSocket

#### 讯飞在线 TTS

配置类：`XFOnlineTTSConfig`

- 默认端点：`wss://tts-api.xfyun.cn/v2/tts`
- 当前播放器要求 `audioEncoding = raw`
- 默认 `audioFormat = audio/L16;rate=16000`
- 在线 TTS 是一次文本一个 WebSocket 会话，所以项目会把 DeepSeek delta 批成多个短句顺序合成

#### 豆包 TTS

配置类：`DoubaoTTSConfig`

- 默认端点：`https://openspeech.bytedance.com/api/v3/tts/unidirectional/sse`
- 默认 `resourceId = seed-tts-2.0`
- 当前播放器要求 `audioFormat = pcm`
- 支持 `speaker`、`speechRate`、`loudnessRate`、`emotion`、`emotionScale`
- 支持 `additions.context_texts` 语音指令；可用静态指令，也可先让 DeepSeek 根据用户文本和情绪快照生成一句语音指令
- `useSectionId` 开启时，会用当前会话 ID 作为稳定上下文 ID

### Azure Speech

高级设置 → **Azure TTS**：填写 Speech 资源的 Key 和区域代码（例如 `eastasia`，必须与资源实际区域一致），设置音色后点击 **试听 Azure**。试听使用当前草稿，不修改已保存的提供方；点击 **选择 Azure 朗读**，再 **应用并保存**即可启用。**停止试听**会立即清除音频并取消请求。

- 默认音色：`zh-CN-XiaoxiaoNeural`，语言：`zh-CN`。区域和密钥默认留空。
- 风格留空或 `neutral` 使用自然语气；可填写音色支持的 `cheerful` 等风格。服务会查询当前区域的音色列表并验证风格，缓存 10 分钟。
- 风格强度范围 `0.01–2`；语速变化 `-50–100%`；音调变化 `-50–50%`；音量 `0–100`。音色和风格支持情况以 Azure 为准。
- 使用官方 REST `https://{region}.tts.speech.microsoft.com/cognitiveservices/v1`，SSML 输入、`raw-24khz-16bit-mono-pcm` 流式输出。无需另装 Speech SDK 或解码器。
- 文本按短句顺序合成，音频边到边播；首段目标 12 字／最长等待 180ms，后续目标 24 字／最长等待 300ms，单段上限 80 字。可以在配置资产或 JSON 的 `azureTts` 段调整分段参数。
- 默认首包和连续无音频超时 10 秒，单段总超时 30 秒。鉴权、限流、音色不支持、空音频、连接失败会停止朗读，保留模型的文字回答；不自动重播已开始的句子。
- 本版范围为全球版预置音色与手动 SSML 语气控制，不包含自定义声音、原生 TextStream 或自动情绪分析。

配置文件仍为 `Application.persistentDataPath/RuntimeConfig/voice-chat-settings.json`。Windows 本项目的默认位置为 `%USERPROFILE%/AppData/LocalLow/Camelminger/customDigitalman/RuntimeConfig/voice-chat-settings.json`。schema 5 会兼容旧配置，补齐 `azureTts`、`playback`，不改变已有 TTS 选择。真实密钥请通过本机设置填写，不写入提交的资产。

官方参考：[REST 接口](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/rest-text-to-speech)、[SSML 风格与参数](https://learn.microsoft.com/zh-cn/azure/ai-services/speech-service/speech-synthesis-markup-voice)。

## 口型、音频和动画

`StreamingPcmAudioPlayer` 使用非 streamed 的动态 `AudioClip`：

```text
TTS raw PCM -> 带会话编号的有界音频队列 -> AudioClip.SetData() -> AudioSource -> SALSA.GetData()
```

高级设置 → **音频缓冲**提供四种 TTS 共用的参数：起播默认 `0.12s`、补缓冲恢复门槛默认 `0.20s`、待写音频队列上限默认 `120s`。30 秒环形 AudioClip 只写入空闲区，已经消费的区域清零；DSP 定时结束边界防止主线程卡顿时播放越过有效数据。输入结束后会排空尾音再停止，补缓冲恢复不会从头重播或重新随机说话动作。

网络抖动仍可能产生停顿：可适当增加起播／恢复门槛，以首音等待时间换取连续性。队列超限会明确报错并停止本轮朗读，不覆盖未播放样本。播放器只接受单声道 PCM16，保留跨包残余字节；取消、退出、换流会清理队列和旧 AudioClip。

开启“输出音频性能诊断”后，正常播放结束会记录首 PCM、首音时间、补缓冲次数和队列峰值。首音指标包含本地等待，不应与云端合成耗时混为一谈。

这样做是为了兼容 SALSA。SALSA 会对 `AudioSource.clip` 调用 `AudioClip.GetData()`，Unity streamed clip 不支持这个调用。不要把当前 TTS 播放器改回 `AudioClip.Create(..., stream: true, ...)`。

重要绑定：

```text
VoiceChatSystem.AudioSource
 -> StreamingPcmAudioPlayer.audioSource
 -> VoiceChatController.ttsAudioSource
 -> 各角色 SALSA.audioSrc
```

TTS 播放开始时 `VoiceChatController` 会把 Animator bool `isTalk` 设为 `true`，结束或打断时设为 `false`。如果 Animator 有 int 参数 `talkAnimationIndex`，每次开始说话会随机选择一个说话动作变体。

## 界面与连接测试

运行时界面全部由代码生成：`AssistantUi`（配色、圆角、按钮、开关、输入框、卡片等基础组件）+ `AssistantSettingsHub`（设置中心）+ `AssistantDashboard`（对话面板，消息以气泡显示，分“对话 / 历史 / 知识库”三个标签）。场景里的“切换场景”“退出”等按钮会被统一换成同样的样式；对话面板会自动停在“退出”按钮上方。

每个服务账号的编辑页都有“测试连接”：用当前填写（尚未保存）的信息发起一次真实请求，结果显示在页面上，账号列表与卡片上也会标出“连接正常 / 测试未通过”。测试由 `ServiceConnectionTester` 完成，不保存任何配置：

- 聊天服务：请求 `/models`；接口不提供模型列表时，用已选模型发送一条极短的消息。
- 语音合成（Azure、豆包、讯飞）：合成“你好。”，收到第一段音频即判定可用。
- 讯飞语音识别：建立签名连接并发送 0.2 秒静音。
- 百度搜索：执行一次只取 1 条结果的搜索。

常见失败（鉴权失败、区域不匹配、额度不足、网络不可达、讯飞错误码等）会翻译成中文提示，并附带原始信息。「意识」页另有“测试当前模型”，按当前模型参数实际请求一次。

验证：`AssistantUiValidation.Run`（离线，含本地回环假服务）把各页面预览写到 `.work/ui-preview`；`AssistantScenePreview.Run` 用隔离的示例配置运行主场景并截图，不读取也不修改本机配置与聊天记录。

## VRM 角色

除 CC 角色 `test` / `girl` 外，可以加入 VRM（VRoid 等）角色，依赖 UniVRM `v0.131.3`（`com.vrmc.gltf`、`com.vrmc.vrm`，见 `Packages/manifest.json`）。

1. 把 `.vrm` 放到 `Assets/MyCharacter/VRM/`。导入器固定使用 URP 材质（MToon10），VRM 0.x 会自动迁移为 1.0。
2. 菜单 `Tools/Voice Chat/Install VRM Characters`：把每个文件实例化到 `main.unity`，追加到 `CharacterModelSwitcher`，挂 `VrmMouthDriver`，并复用当前 CC 角色的 Humanoid 说话 Animator。可重复运行，不会重复添加。
3. 运行后用「切换模型」按钮在所有角色间轮换。

VRM 只有 aa / ih / ou / ee / oh 五个元音表情，`VrmMouthDriver` 负责口型：

- Azure 连续口型：`AlignedMouthPlayback` 通过 `IAlignedMouthTarget` 把 55 通道嘴部数据折算成五个元音。
- 讯飞 / 豆包 / 普通 Azure：分析 TTS `AudioSource` 的响度和频谱，估计元音和张嘴幅度，不需要 SALSA。
- 自动眨眼；强度、开合速度在组件 Inspector 中调整。

安装工具会给 VRM 网格开启 `updateWhenOffscreen`：VRM 网格的包围盒相对静止的 `Root` 骨骼，而说话动画会让身体平移约 0.3 m，镜头只框住脸时脸会被剔除。

验证：`-executeMethod VrmCharacterValidation.Run -vrmFolder <文件夹>` 在不保存的场景副本中检查安装结果、连续口型、音频口型，并在 `.work/vrm-test` 输出脸部截图（`.work/vrm-test/v_sent.wav` 存在时才做音频检查）。

## 情绪识别

MoodMe 运行时由 `FaceDetector`、`ManageEmotionsNetwork`、`EmotionsManager` 组成，HUD 由 `MoodMeEmotionHudPresenter` 刷新。`VoiceChatController` 在用户按住说话键期间采样：

```text
Happy / Surprised / Scared / Angry / Disgust / Sad / Neutral
```

如果 `includeEmotionInPrompt = true`，每轮用户文本会附带平均情绪快照，让 DeepSeek 调整语气和共情程度。该信息也会用于豆包 TTS 的可选语音指令生成。

编辑器菜单 `Tools/MoodMe/Install Emotion HUD` 对应 `MoodMeEmotionHudSceneInstaller`，用于把 MoodMe runtime 和 HUD 安装到 `main.unity`。

## 会话记忆

会话持久化默认开启。

```text
Application.persistentDataPath/
  VoiceChat/
    Conversations/
      yyyy-MM-dd_HH-mm.json
```

每个会话记录 `user`、`assistant` 和隐藏 `system` 消息。流式 assistant 内容会节流保存，默认间隔 `0.75s`。加载历史时，`streaming` 状态会规范化为 `interrupted`。

Prompt 里默认只保留 DeepSeek 配置指定的最近消息数量，较早消息会通过 `TextSearchUtility` 做关键词相关性召回。

## 本地知识库

知识库默认目录：

```text
Application.persistentDataPath/VoiceChat/KnowledgeBase/
```

支持文件：

```text
.txt
.json
```

JSON 会被扁平化为 `path: value` 文本。JSON 顶层可选字段包括：

```text
enabled
alwaysInclude / always / global
title / name
category / type
```

默认行为是：新会话第一次用户发言前，把当前知识库完整快照作为隐藏 system 消息注入。涉及校园资料、流程、地点、联系方式等问题时，DeepSeek 会优先依据本地知识库；知识库不足时再考虑联网搜索。

## Agent 工具

`VoiceChatController` 暴露给 DeepSeek 的本地工具：

- `get_current_datetime`：返回本机日期、时间、时区和中文星期。
- `web_search`：调用百度千帆 AI Search Web Search。

默认最多 `2` 轮工具决策。涉及今天、现在、本周、本月、今年等问题时应先用日期工具；涉及新闻、公告、价格、天气、比赛、官网信息等可能变化的信息时可用 `web_search`。

百度搜索配置类：`BaiduSearchConfig`

默认端点：

```text
https://qianfan.baidubce.com/v2/ai_search/web_search
```

搜索结果会被整理成自然语言上下文和 JSON 工具结果，再交给 DeepSeek 生成最终回答。

## 主要脚本索引

- `Assets/Scripts/VoiceChatController.cs`：主流程、输入、ASR、DeepSeek、TTS、打断、情绪、记忆、知识库和工具编排。
- `Assets/Scripts/Services/StreamingPcmAudioPlayer.cs`：raw PCM 到非 streamed `AudioClip` 的动态播放。
- `Assets/Scripts/Services/XFASRService.cs`：讯飞 ASR WebSocket 客户端。
- `Assets/Scripts/Services/DeepSeekService.cs`：DeepSeek Chat Completions，支持 SSE 和 tool calls。
- `Assets/Scripts/Services/XFTTSService.cs`：讯飞超拟人 TTS。
- `Assets/Scripts/Services/XFOnlineTTSService.cs`：讯飞在线 TTS。
- `Assets/Scripts/Services/DoubaoTTSService.cs`：豆包 TTS V3 SSE。
- `Assets/Scripts/Services/BaiduSearchService.cs`：百度千帆联网搜索。
- `Assets/Scripts/Services/ChatHistoryStore.cs`：会话 JSON 存取和历史相关性召回。
- `Assets/Scripts/Services/KnowledgeBaseStore.cs`：本地知识库扫描、JSON 扁平化和上下文生成。
- `Assets/Scripts/Config/VoiceChatRuntimeConfigStore.cs`：运行时配置文件加载、保存、应用和 schema 默认值。
- `Assets/Scripts/UI/AdvancedSettingsPanel.cs`：运行时高级设置面板。
- `Assets/Scripts/UI/VoiceChatMemoryPanel.cs`：会话和知识库状态面板。
- `Assets/Scripts/UI/MoodMeEmotionHudPresenter.cs`：摄像头预览、人脸框和情绪条。
- `Assets/Scripts/CharacterModelSwitcher.cs`：数字人模型切换、SALSA 音源同步和 Animator 重绑。
- `Assets/Scripts/CharacterCameraPositioner.cs`：把角色放到摄像机前方。
- `Assets/Scripts/SceneFadeManager.cs`：入口场景淡入淡出切换。
- `Assets/Scripts/SkyboxCubemapCycle.cs`：入口场景天空盒自动循环。
- `Assets/Scripts/VRCamera.cs`：鼠标拖拽视角。
- `Assets/Scripts/ClickCharacter.cs`：点击角色触发动作。
- `Assets/Scripts/QuitButton.cs`：编辑器停止播放或打包后退出程序。

## 资源目录

- `Assets/MyCharacter/test/Prefabs/Camelmingerup.prefab`：主场景模型 `test`。
- `Assets/MyCharacter/girl/Prefabs/female4_0.prefab`：主场景模型 `girl`。
- `Assets/MyAssets/Animator/`：Idle、Talking、Waving、Angry、Bow 等动画资源和控制器。
- `Assets/MyAssets/Materials/`：入口/场景 UI 图片、球内全景材质和 `Custom/SkyboxCubemapBlend`。
- `Assets/Plugins/Eight Realm Horizons - 360 Skybox Pack/Skybox_Pic/`：咨询室、沙漠、草原、体育馆、冰原、火星、月球、宇宙等 360 资源。
- `Assets/Plugins/`：第三方插件源码和示例，除非任务明确要求，不建议随意修改。

## 常见问题

### ASR 报 `10404: no category route found`

通常是讯飞能力路由不匹配。检查：

- `websocketUrl = wss://iat.xf-yun.com/v1`
- 应用已开通中文 ASR 大模型 WebAPI
- `AppId / ApiKey / ApiSecret` 来自同一个已开通应用
- `domain = slm`
- `language = zh_cn`
- `resId` 先留空

### 有声音但 SALSA 口型不动

检查：

- SALSA `audioSrc` 是否指向 `VoiceChatSystem.AudioSource`
- `StreamingPcmAudioPlayer.audioSource` 是否也是同一个 `AudioSource`
- TTS 输出是否是 raw PCM，不是 MP3、lame 或其他编码
- `AudioSource.clip` 是否有数据
- 当前模型是否启用，SALSA 组件和 viseme 配置是否有效

### TTS 没声音

检查：

- 当前 `TTS Provider` 是否和配置资产一致
- 对应 API Key / endpoint / voice / sampleRate 是否来自同一个已开通服务
- 讯飞两种 TTS 要求 `audioEncoding = raw`
- 豆包 TTS 要求 `audioFormat = pcm`
- `sampleRate` 必须和服务返回音频一致

### 百度搜索工具没有结果

检查：

- `BaiduSearchConfig.apiKey` 是否填写
- 授权头默认是 `Authorization: Bearer <API Key>`，并兼容 `X-Appbuilder-Authorization`
- 千帆端是否开通 AI Search Web Search
- 查询文本会按配置裁剪，默认最多约 `72` 个百度检索字符

## 维护注意

- 如果修改场景，请直接修改 `.unity` 场景文件中实际对象和组件，不要只靠运行时脚本临时实例化来替代场景改动。
- 不要提交真实 API Key。
- 不要随意改 `Assets/Plugins` 下的第三方插件，除非任务本身就是插件适配。
- 当前播放器只处理 16-bit little-endian PCM。要支持 MP3/lame 等格式，需要新增解码层。
- 包含中文的文件建议保持 UTF-8；编辑 C# 字符串、README 或 Unity YAML 前先确认编码，避免再次出现乱码。

## Azure 与音频验证

使用 Unity 2022.3.62f3 的 `-batchmode -projectPath <项目绝对路径> -executeMethod <方法> -logFile <日志路径>`：

- `AzureTtsValidation.Run`：离线传输、SSML、随机 PCM 分包、加速模拟 10 分钟音频环、溢出、取消、配置迁移与场景绑定。异步入口会自行退出，不加 `-quit`。
- `AzureTtsPlayModeValidation.Run`：加 `-forceAudio`，在空场景运行真实播放时钟和 AudioSource 输出检测，覆盖 100/300/1000ms 断流、跨环及 50 次打断。入口自行退出；不访问订阅。
- `ChatFeatureValidation.Run`：先启动 `python Tools/chat_mock_server.py`，回归已有聊天功能，入口自行退出。
- `AzureTtsBuildValidation.Build`：加 `-quit`，构建 Windows x64 开发版到项目旁 `garbage/AzureWindows/`。
- `AzureTtsBuildValidation.Preview`：加 `-quit`，使用隔离的无密钥配置渲染设置页到项目旁 `garbage/`。
- `AzureTtsSceneSetup.Install`：已执行并保存了实际场景；只有需要重新安装 Azure 控件时才运行此方法或相应 Tools 菜单。

离线验证不能替代真实订阅联调。首次使用请在本机填好区域和 Key，试听中文、中英混合与所需风格，再在实际角色上检查口型。401/403 检查 Key、区域和权限；429 检查配额并稍后重试；不支持的风格可留空恢复自然语气。

## 统一助手界面（2026-09-14）

`main` 场景已保存 `AssistantInterface` 画布，并通过 `AdvancedSettingsPanel.interfaceRoot` 绑定；旧设置入口和旧记忆面板已在场景中停用。重新进入 Play 模式后，新控制面板在右侧显示。账号、模型列表和草稿表单由 uGUI 根据当前配置生成。

- 右侧面板：设置入口、历史会话、聊天输入、记忆与知识库折叠区及横向麦克风电平表。历史与知识库互斥展开，避免挤出输入区；收起后仍可进入设置。
- 设置 → 服务来源：集中管理密钥、地址、区域等信息；同一服务可以建立多个账号。
- 设置 → 机体模块：意识选择服务账号和模型，发声选择账号与音色，听觉/搜索/记忆/知识库保留对应参数。
- 配置使用独立草稿，点击“保存并应用”才启用；关闭未保存的窗口会提示。凭据修改会作用于引用此账号的模型，模型的采样参数仍独立。
- 配置升级到 v6，首次覆盖旧版本前保留同目录 `.v5.bak` 原文件，写入使用临时文件和原子替换。运行时旧服务仍通过兼容适配读取配置。
- DeepSeek 与兼容服务读取模型列表，Azure 读取音色/语言/风格；失败保留缓存和手动 ID。火山方舟、豆包和讯飞保留手动 ID，不假设账号拥有官网展示的全部资源。
- 四种 TTS 均提供草稿试听。试听结束当前回复播放，不修改正式服务选择或聊天历史；关闭或切换页面会取消试听。

验证入口：`AssistantUiValidation.Run` 在隔离测试项目中执行配置/目录列表模拟测试并将界面截图写入 `ValidationArtifacts/`。`AzureTtsPlayModeValidation.Run` 还覆盖统一试听的完成、取消、替换和服务选择保持。测试项目必须使用独立目录，避免影响正在编辑的场景；这些检查使用模拟凭据，不访问真实订阅。
