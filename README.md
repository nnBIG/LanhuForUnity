# Lanhu Runtime Sync

蓝湖设计稿到 Unity UGUI 的编辑器导入与增量更新工具。

## 安装与更新

推荐通过 Unity Package Manager 安装：

```text
https://github.com/nnBIG/LanhuForUnity.git#main
```

在 Unity 中打开 `Window > Package Manager`，点击 `+`，选择 `Add package from git URL` 并粘贴上面的地址。

- Git 安装版本默认每天检查一次远端 `package.json`，发现新版本时可一键更新。
- 可以通过 `Tools > Lanhu Runtime Sync - Check for Updates` 手动检查。
- 可以在 `Preferences > Lanhu Runtime Sync` 中关闭每日检查。
- 直接放在 `Assets` 下的开发副本不会自动覆盖自身，避免与 UPM 包形成重复程序集。
- 发布新版本时必须同时提升 `package.json` 的 `version`，其他项目才能收到更新提醒。

## 使用流程

1. 在 Unity 中打开 `Tools > Lanhu Runtime Sync`，填写蓝湖项目 URL。
2. 私有项目需要填写 Cookie；浏览器登录状态不会自动共享给 Unity。
3. 在浏览器登录蓝湖，从开发者工具的 Network 面板选择一个成功的蓝湖 API 请求。可以复制 Request Headers 中完整的 `Cookie` 值，也可以右键请求并选择 `Copy as cURL`；`-H 'cookie: ...'`、`-b '...'` 和 `--cookie '...'` 格式都可以直接填入窗口，随后点击 `Save Local`。看到格式识别成功的提示后再加载页面；公开项目可以留空。
4. 选择页面后点击 `Import Selected Page`。
5. 后续在生成的预制体或场景根节点上点击 `Pull Latest From Lanhu`，工具会按蓝湖节点 ID 更新原对象，不会重复创建同一页面。

Cookie 只保存在本机 Unity `EditorPrefs` 中，不会写入项目文件。也可以通过本机环境变量 `LANHU_COOKIE` 提供。

蓝湖返回 HTTP 401/403/418 时，通常表示 Cookie 已过期、复制不完整，或该 Cookie 对应的账号没有当前项目权限。重新登录后，应从状态为 200 的 `/api/project/...` 请求复制 Cookie/cURL，而不是图片、统计或失败请求。

## 导入模式

- 蓝湖节点带有导出图片时，工具按图层创建 UGUI 节点，并导入 Sprite、TMP 文本、纯色填充、位置、层级和显隐状态。
- 一个文本节点包含多段颜色、字号、字重、斜体、下划线或删除线时，会转换为 TextMeshPro 富文本标签。
- 字体会按字体族、PostScript 名称、字重与斜体匹配项目中的 TMP Font Asset；行高和字间距也会同步。缺少字体时会在导入报告中列出具体字体，不再静默回退。
- TMP 描边和阴影使用 TextMeshPro SDF 材质参数，不添加 UGUI `Shadow` 或 `Outline` 组件；普通文字使用字体默认材质，效果参数相同的文字复用材质，参数不同则生成独立材质。
- Photoshop 来源的蓝湖阴影会把 Distance、Angle、Spread 和 Blur 转换为 TMP Underlay 的 Offset、Dilate 和 Softness，并按项目的 Gamma/Linear 色彩空间转换效果颜色；导入文字的 Character Spacing 固定为 `0`。
- Prefab 使用 `Canvas > Page > Nodes` 层级；CanvasScaler 的参考分辨率来自蓝湖画板，Page 保持画板原始尺寸。旧版单根节点 Prefab 在下次 Pull 时会自动迁移。
- Photoshop 固定文本框默认关闭 TMP 自动换行和自动字号；Black、Bold 等已由字体资产表达的字重不会再次叠加 TMP 合成粗体。
- 页面没有任何可导出的切图时，默认使用整页预览图保证视觉一致，同时保留不可见的节点绑定层级，便于后续按节点 ID 更新。
- 页面只有图层元数据而没有逐层图片时，导入前会让用户选择：使用整页预览，或仅重建可编辑的 TMP 文本和纯色形状。
- `Skip Hidden Nodes` 开启时不创建蓝湖隐藏节点；关闭时会创建节点并按蓝湖显隐状态设为 inactive。
- `Delete Missing Nodes` 开启时，蓝湖中已删除的绑定节点也会从 Unity 删除。

## 节点级同步

每个导入节点都有 `LanhuRuntimeBinding`。可以分别关闭以下字段，保护 Unity 中的手工修改：

- `Transform`：父子层级、顺序、位置和尺寸。
- `Visibility`：GameObject active 状态。
- `Text`：TMP 文字内容和富文本片段。
- `Image`：Sprite 引用。
- `Style`：颜色、字体、字号、对齐、描边和阴影。

## 已知限制

- 蓝湖没有公开稳定的第三方导入 API，本工具使用当前网页只读接口；蓝湖接口变化后可能需要同步适配。
- 未标记导出图片的形状、复杂效果和合成图层无法从 JSON 精确重建，只能使用整页预览图回退。
- 字体需要项目中已有匹配名称和字重的 TMP Font Asset，否则使用项目默认 TMP 字体并输出警告。一个蓝湖文本节点内混用多个字体族时，TMP 无法可靠地按片段切换 Font Asset，建议在蓝湖中拆为多个文本图层。
- 自动导入的图片关闭 Mipmap 和 Read/Write，并使用压缩纹理；SpriteAtlas 建议按页面或功能模块单独配置，避免跨页面混图。
