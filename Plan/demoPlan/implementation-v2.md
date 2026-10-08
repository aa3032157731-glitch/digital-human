# 第一人称实训 v2 实现与交付

## 启动

- Unity 版本：2022.3.62f3。
- 场景：`Assets/FactoryLab/Scenes/FactoryLab.unity`。
- Windows 程序：`Builds/FactoryLab/FactoryLab.exe`。分发时保留整个 `Builds/FactoryLab` 文件夹。
- 控制器默认 `firstPersonCourse = true`；原仿真内核和旧课程类型保留用于兼容回归，新入口不显示旧主观题表单。

## 操作

1. 点击“继续行走”，用 WASD 行走、鼠标转头，靠近老师后瞄准并按 E。
2. 点击“我准备好了”接受当前任务。按 Tab 查看任务，Esc 暂停并释放鼠标。
3. 到指定工位，瞄准工位实体并按 E 观察；从工位窗口可进入 LAMP-01 实际事件列表。
4. 第三环节在订单控制台启动原始 20 件订单。默认 5×，可选择 1×、5×、10×播放，播放速度不改变结果。完成后查看事件，再答题。
5. 第四环节从事件列表选择一条搬运及一条等待记录。
6. 第五环节点击“现场选择两个工位”，回到现场依次瞄准两个作业工位按 E，预览后确认重置并交换，再选择三项趋势预测。
7. 逐题纠错完成后回到老师处确认下一站。第六环节运行调整后订单；变差的布局同样允许完成。
8. 暂停菜单支持复习已完成内容、导出报告，以及归档当前记录后重新开始。

对话、答题、设置、交换确认和窗口失焦都会停止行走并暂停订单。关闭窗口不会自动开始行走；需点击继续。老师在学生确认下一站后淡出切换位置。

## 代码分工

| 文件 | 职责 |
|---|---|
| `FactoryFirstPerson.cs` | CharacterController、视高/速度/视野、鼠标锁定、2 米无遮挡交互射线、统一交互接口 |
| `FactoryCourseV2.cs` | 六环节推进门槛、20 道题、证据快照、尝试/提示/两种成绩、版本化预测、HTML 报告 |
| `FactoryWalkCourse.cs` | 第一人称 HUD、教学对话、现场观察、事件选择、交换预览、安全移动、独立存档、复习 |
| `FactoryWorldText.shader` | 带深度检测的中文场景标识，避免文字穿透人物和设备 |
| `FactoryV2Validation.cs` | 编辑器中的题库、评分、数值绑定、完整课程与布局回归 |
| `FactoryV2PlayerValidation.cs` | 显式启动的独立程序自动验收：PhysX、交互射线、真实 UI 回调、存档与渲染 |

`VoiceChatController.SpeakTeachingTextAsync` 直接接收指定话术，复用现有 TTS、PCM 播放、口型和取消机制，不经过大模型。离开对话会取消录音及朗读；在线问答仍使用课程/实验快照，不具有修改分数或布局的工具权限。

## 数据与评分

- 共 20 道，每题 5 分；10 单选、6 判断、4 数值题。
- 每道题第一次提交决定首次分；首次答错后答对只增加掌握分。已答对的重复提交不会新增尝试或分数。
- 数值题允许整数、小数和负数，误差 ±0.1；拒绝空字符串、NaN、Infinity 等无效数值。
- 四道数值题绑定实际检验事件、原始完成快照、当前布局路线及两次同参数完成快照。
- 数值题 q17 绑定后停止继续交换，防止已评分的路线题与当前布局不一致；趋势预测在开跑时锁定并绑定运行 ID。
- 玩家和仿真调度相互独立。动态产品及搬运车不产生玩家碰撞；墙、工位和货架使用整块碰撞体。

## 存档与报告

新存档：`Application.persistentDataPath/FactoryLab/Course-v2/progress.json`，采用临时文件 + 原子替换，并保留 `.bak`。

报告：同目录 `Reports` 中的 HTML 和 JSON，包含双成绩、题目尝试、提示使用、观察/任务状态、事件证据、布局版本、预测与结果比较。旧版 `Course` 不参与新成绩计算，旧报告不删除。

恢复时重建布局、恢复完成快照与答题记录，将玩家放回安全教学点；未完成订单从零重新运行，界面会说明。存档结构无效时显示原因，不把旧草稿换算成新成绩。

## 可重复验收

从项目目录运行 Unity 批处理，分别指定：

```text
-executeMethod FactoryLab.Editor.FactoryLabTools.Validate
-executeMethod FactoryLab.Editor.FactoryV2Validation.Validate
-executeMethod FactoryLab.Editor.FactoryV2Validation.ValidateAndBuild
```

以上使用 `-batchmode -quit -projectPath "项目绝对路径" -logFile "日志绝对路径"`。

独立程序自动验收参数：

```text
FactoryLab.exe -factory-v2-selftest -screen-width 1366 -screen-height 768 -logFile "日志绝对路径"
```

自动验收存档使用独立 `FactoryLab/V2Acceptance/<时间戳>`，不覆盖学生课程。

## 验收边界

- 仿真回归：原始／参考布局单件 174／158 秒，20 件 1771／1454 秒；原始／参考载货距离 680／360 米；24 种合法布局数量守恒且无死锁。
- 编辑器自动课程检查：见 `.work/factory-lab/v2-validation.json`。
- Windows 构建：见 `.work/factory-lab/build-result.json`。
- 独立程序自动验收结果及图片：见运行日志输出的 `V2Acceptance` 目录。
- 1366×768、1920×1080、2560×1440 的自动渲染与实际人工键鼠验收分别记录，不将截图或自动回调视为人工验收。
- 真实 ASR、在线问答与 TTS 服务验收尚未完成，需配置可用接口和麦克风后验证。现有合成语音链路回归也不等同真实服务验收。
- 当前环境的 Windows 交互工具启动时报 `trusted Node process exited unexpectedly`，因此未声称已完成人工键鼠全流程。程序提供可见窗口供直接试用。

## 本次交付验收结果（2026-09-23）

| 检查 | 结果 | 证据 |
|---|---|---|
| 仿真内核回归 | 9 组通过，包含全部 24 种合法布局 | `.work/factory-lab/validation.json` |
| 题库、评分、课程、六工位射线 | 106 项通过 | `.work/factory-lab/v2-validation.json` |
| Windows 独立程序完整课程、碰撞、UI 及保存 | 125 项通过 | `.work/factory-lab/v2-runtime-result.json` |
| 独立程序重新启动并恢复真实存档 | 6 项通过 | `.work/factory-lab/v2-restore-result.json` |
| 离线合成 TTS、PCM、口型、取消及失败/超时 | 8 组通过，无真实网络调用 | `.work/factory-lab/voice-regression.json` |
| Windows x64 构建 | 成功，0 错误、0 警告 | `.work/factory-lab/build-result.json` |
| 人工键鼠、真实 ASR/TTS | 未验证 | 上述验收边界 |

独立程序最终自动验收截图与详细报告在 `C:/Users/qqcom/AppData/LocalLow/Camelminger/customDigitalman/FactoryLab/V2Acceptance/20260923-002133`。正式试用使用独立的正常课程目录，不载入自动测试成绩。