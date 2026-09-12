# Eight Realm Horizons - 360 Skybox Pack / 八境天幕 - 360天空盒资源包

## English

Eight Realm Horizons is a set of eight AI-assisted, manually enhanced 360-degree skybox textures for Unity. The images were generated with AI tools, then personally reviewed, repaired, cleaned, and upscaled by the publisher to improve visual quality and practical usability in Unity projects.

This package was authored in a Unity `2022.3.62f` URP project. The included assets are standard panoramic PNG textures and Unity skybox materials, so they are not tied to a custom script, package, or runtime dependency.

### Included Skyboxes

| File | English Name | Theme |
| --- | --- | --- |
| `体育馆.png` | Stadium | Indoor sports arena |
| `冰原.png` | Icefield | Frozen landscape |
| `咨询室.png` | Consultation Room | Interior room environment |
| `宇宙.png` | Cosmos | Deep space scene |
| `月球.png` | Moon | Lunar surface |
| `沙漠.png` | Desert | Dry open landscape |
| `火星.png` | Mars | Red planet terrain |
| `草原.png` | Grassland | Open meadow landscape |

### Package Contents

- 8 equirectangular 360 panorama textures in PNG format.
- 8 ready-to-use Unity skybox materials in `Materials/`.
- Unity `.meta` files for consistent import settings.
- This README and license notice.

### Texture Details

- Source texture size: `7240 x 3620` pixels.
- Layout: latitude-longitude / equirectangular 360 panorama.
- Format: PNG.
- The included Unity import settings currently generate cubemap data and use a default max texture size of `2048`. Users may raise the max texture size in Unity if the target platform and memory budget support higher-resolution skyboxes.

### Unity Version and Render Pipeline Compatibility

Authored and checked in:

- Unity `2022.3.62f`
- Universal Render Pipeline (URP)

Expected compatibility:

| Environment | Status | Notes |
| --- | --- | --- |
| Unity 2022.3 LTS + URP | Supported / authored environment | This is the source project environment. |
| Built-in Render Pipeline | Expected to work | Unity's Panoramic skybox shader is supported in the Built-in Render Pipeline. |
| URP in other supported Unity versions | Expected to work | Unity's Panoramic skybox shader is supported in URP. Users should verify lighting and import settings per project. |
| HDRP | Textures usable, included materials not directly compatible | Unity documentation states HDRP does not support traditional skybox Materials. HDRP users should use the PNG panoramas as source textures in HDRP's sky/environment workflow and recreate project-specific settings. |

Unity documentation states that skyboxes and the Panoramic skybox shader are supported in the Built-in Render Pipeline and URP, while HDRP uses different sky generation systems instead of traditional skybox Materials.

References:

- Unity Manual - Skyboxes: https://docs.unity.cn/2022.3/Documentation/Manual/skyboxes.html
- Unity Manual - Panoramic skybox: https://docs.unity.cn/2022.1/Documentation/Manual/shader-skybox-panoramic.html
- Unity Asset Store Submission Guidelines: https://assetstore.unity.com/publishing/submission-guidelines

### Unity Usage

1. Import the package into a Unity project.
2. Open the `Materials/` folder.
3. Select one of the included skybox materials.
4. Assign it through `Window > Rendering > Lighting > Environment > Skybox Material`, or assign it by script with `RenderSettings.skybox`.
5. Adjust material exposure, tint, rotation, or texture import size as needed for the target project.

### AI Content Disclosure

This package contains AI-assisted generated visual content. The eight panoramic skybox textures were initially generated using AI image generation tools, then manually enhanced by the publisher through visual inspection, artifact cleanup, image-quality repair, and resolution upscaling.

The package is not presented as purely human-painted artwork. The added publisher work consists of selection, manual quality repair, resolution enhancement, Unity import preparation, skybox material setup, naming, packaging, documentation, and compatibility notes.

The exact AI tools used for generation are disclosed in the Unity Asset Store product listing's AI description field, as required by the Unity Asset Store Submission Guidelines.

### Rights and Commercial Use Notice

The final package is owned or fully controlled by the publisher for commercial distribution as a Unity asset. No third-party logos, brands, characters, copyrighted artworks, trademarked objects, stock photos, externally licensed media, or open-source components are intentionally included.

When this package is obtained through the Unity Asset Store, end users may use it in personal or commercial projects only as allowed by the Unity Asset Store EULA. This includes using the assets as embedded components in games, applications, simulations, rendered media, and other licensed products that contain substantial original content beyond the assets themselves.

The Unity Asset Store EULA does not allow the assets to be resold, redistributed, sublicensed, shared, repackaged, or made available as standalone files or as part of another asset pack except where expressly permitted by the EULA.

For commercial uses outside the Unity Asset Store EULA, including standalone redistribution, resale, sublicensing, inclusion in another asset pack, custom enterprise licensing, exclusive licensing, or any separate non-Asset Store license, contact:

`camelmingerup@gmail.com`

See `LICENSE.md` for the license notice.

---

## 中文

八境天幕是一个包含 8 张 360 度天空盒贴图的 Unity 资源包。贴图由 AI 工具辅助生成，并由发布者亲自进行画质检查、瑕疵修复、画质优化和分辨率提升，以提高视觉质量和 Unity 项目中的实际可用性。

本资源包制作于 Unity `2022.3.62f` 的 URP 项目。包内资源主要是标准全景 PNG 贴图和 Unity 天空盒材质，不依赖自定义脚本、运行时代码或额外插件。

### 包含的天空盒

| 文件 | 英文名称 | 主题 |
| --- | --- | --- |
| `体育馆.png` | Stadium | 室内体育馆 |
| `冰原.png` | Icefield | 冰雪地貌 |
| `咨询室.png` | Consultation Room | 室内咨询室环境 |
| `宇宙.png` | Cosmos | 深空宇宙 |
| `月球.png` | Moon | 月球表面 |
| `沙漠.png` | Desert | 沙漠地貌 |
| `火星.png` | Mars | 火星地貌 |
| `草原.png` | Grassland | 草原地貌 |

### 资源内容

- 8 张 PNG 格式的等距柱状投影 360 全景天空盒贴图。
- `Materials/` 文件夹内的 8 个 Unity 天空盒材质。
- 用于保持导入设置一致的 Unity `.meta` 文件。
- 本 README 与授权说明文件。

### 贴图信息

- 源贴图尺寸：`7240 x 3620` 像素。
- 布局：Latitude-Longitude / Equirectangular 360 全景图。
- 格式：PNG。
- 当前随包提供的 Unity 导入设置启用了 cubemap 生成，默认最大贴图尺寸为 `2048`。如果目标平台性能和显存预算允许，用户可在 Unity 中提高最大贴图尺寸。

### Unity 版本与渲染管线兼容性

制作与检查环境：

- Unity `2022.3.62f`
- Universal Render Pipeline (URP)

预期兼容性：

| 环境 | 状态 | 说明 |
| --- | --- | --- |
| Unity 2022.3 LTS + URP | 已制作环境 / 支持 | 本资源包的源项目环境。 |
| Built-in Render Pipeline | 预期可用 | Unity 官方文档说明 Panoramic skybox shader 支持 Built-in Render Pipeline。 |
| 其他受支持 Unity 版本中的 URP | 预期可用 | Unity 官方文档说明 Panoramic skybox shader 支持 URP。具体光照和导入设置建议按项目检查。 |
| HDRP | 贴图可复用，随包材质不直接兼容 | Unity 官方文档说明 HDRP 不支持传统 skybox Material。HDRP 用户可将 PNG 全景贴图作为源贴图，在 HDRP 的天空/环境系统中重新配置项目专用设置。 |

Unity 官方文档说明，Skybox 和 Panoramic skybox shader 支持 Built-in Render Pipeline 与 URP；HDRP 使用不同的天空生成系统，不直接支持传统 skybox Material。

参考资料：

- Unity Manual - Skyboxes: https://docs.unity.cn/2022.3/Documentation/Manual/skyboxes.html
- Unity Manual - Panoramic skybox: https://docs.unity.cn/2022.1/Documentation/Manual/shader-skybox-panoramic.html
- Unity Asset Store Submission Guidelines: https://assetstore.unity.com/publishing/submission-guidelines

### Unity 使用方式

1. 将资源包导入 Unity 项目。
2. 打开 `Materials/` 文件夹。
3. 选择需要使用的天空盒材质。
4. 通过 `Window > Rendering > Lighting > Environment > Skybox Material` 指定天空盒材质，或在代码中使用 `RenderSettings.skybox` 指定。
5. 根据项目需要调整材质曝光、色调、旋转或贴图导入尺寸。

### AI 内容披露

本资源包包含 AI 辅助生成的视觉内容。8 张全景天空盒贴图最初由 AI 图像生成工具生成，随后由发布者进行人工筛选、画面检查、瑕疵修复、画质优化和分辨率提升。

本资源包不声明为完全人工绘制作品。发布者的增值工作包括素材筛选、人工画质修复、分辨率提升、Unity 导入设置整理、天空盒材质配置、命名、打包、文档编写和兼容性说明。

实际使用的 AI 生成工具名称会按照 Unity Asset Store Submission Guidelines 的要求，在 Unity Asset Store 商品页的 AI description 字段中披露。

### 权利与商业使用声明

最终资源包由发布者拥有或完整控制，并可作为 Unity 资源进行商业分发。资源中不应有意包含第三方 Logo、品牌、角色、受版权保护的艺术作品、商标物件、图库照片、外部授权媒体或开源组件。

如果本资源包通过 Unity Asset Store 获取，终端用户只能在 Unity Asset Store EULA 允许的范围内将其用于个人或商业项目。这包括将资源作为嵌入组件用于游戏、应用、模拟项目、渲染媒体以及其他包含大量原创内容的授权产品。

Unity Asset Store EULA 不允许将本资源作为独立文件、或作为其他资源包的一部分进行转售、再分发、再授权、共享或重新打包，除非 EULA 明确允许。

如需进行 Unity Asset Store EULA 范围之外的商业使用，包括独立再分发、转售、再授权、加入其他资源包、自定义企业授权、独占授权，或任何独立于 Asset Store 的授权，请联系：

`camelmingerup@gmail.com`

请参阅 `LICENSE.md` 中的授权说明。
