# Outline 工程记忆

本工程的 `ProjectSettings/ProjectVersion.txt` 当前记录 Unity 2021.3.45f2，URP 版本为 12.1.15，已接入 Unity MCP。深度法线描边示例位于 `Assets/DepthNormal_PP/`。打开其中的 `DepthNormal.unity`，通过 **Game** 视图查看主相机效果。

## 记忆维护

完成会影响后续工作的实现、配置调整、技术评估或验证后，同步更新本文件。记录当前实现、已确认的取舍、验证环境和结果，并注明待验证事项。区分代码默认值和示例资产的调参值。

## 代码规范

后续生成的 C#、C++、C 代码使用匈牙利命名风格。变量声明须显式写出类型，避免使用 C# 的 `var` 或 C++ 用于变量类型推断的 `auto`。

| 类型 | 变量名前缀 |
| --- | --- |
| `int` | `i`。无符号类型在对应前缀前再加 `u`，例如 `uint uiCount` |
| 数组、`List`、`Dictionary`、`HashSet` | 分别为 `arr`、`list`、`map`、`hs` |
| `Vector2`、`Vector3`、`Vector4` | 分别为 `vec2`、`vec3`、`vec4` |
| `Matrix3x3`、`Matrix4x4` | 分别为 `mat3`、`mat4` |
| `GameObject`、`Transform`、`Material` | 分别为 `go`、`tf`、`mat` |
| 其他一般对象 | `k` |

成员变量的作用域前缀写在类型前缀之前。

- 类的实例成员变量使用 `m_`，例如 `private Material m_matOutline;`。
- 静态成员变量使用 `ms_`，例如 `private static int ms_iPassIndex;`。
- 继承自 `MonoBehaviour` 的类，其 public 实例成员变量使用 `_`，例如 `public Transform _tfTarget;`。

生成 Shader 时遵循 Unity 的 ShaderLab 和 HLSL 格式与命名惯例。

## 场景与调参

- `OutlineObject` 是描边分组，当前包含建筑外墙和屋顶。将不透明网格移入或移出该节点即可改变描边范围。`NormalObject` 下的红色方块和绿色胶囊用于检查普通物体的渲染与遮挡。
- `DepthNormalOutlineGroup.cs` 在目标相机剔除前，将组内启用的 Renderer 临时切换到 `DepthNormalOutline` 层，渲染结束后恢复原层。该层在本项目中为第 8 层。
- 主相机使用 `DepthNormalRenderer.asset`。它在 Performant、Balanced 和 High Fidelity 三套 URP 配置中的 Renderer 索引均为 1。
- 在该资产的 **Depth Normal Outline** 中调整算法、质量、颜色、采样半径和阈值。新建配置默认使用 Sobel、High、FXAA、2.0 像素采样半径、相对深度阈值 0.025 和法线阈值 0.4。截至 2026-11-24，示例资产使用 Sobel、High、FXAA、2.6 像素采样半径、相对深度阈值 0.02 和法线阈值 0.8。
- **Width Pixels** 对应 `m_fWidthPixels`，接受 1.0 至 8.0 的浮点值，单位为相机渲染像素。旧资产的 `widthPixels` 或 `m_iWidthPixels` 整数值在加载时通过隐藏字段 `m_iLegacyWidthPixels` 迁移。
- **AA Mode** 从上到下依次为 None、Coverage、FXAA、SMAA 1x，新建配置和示例 Renderer 均默认选 FXAA。None 直接合成原始描边，跳过 AA 覆盖率和 SMAA 中间 Pass。原先关闭 Anti Aliasing 的配置加载后会迁移为 None。
- Coverage 保留原有滤波。FXAA 在合成时沿遮罩边缘搜索，与 Coverage 的 Pass 数相同。SMAA 1x 在遮罩分辨率下增加边缘检测和混合权重两个 Pass，使用 URP 的 AreaTex、SearchTex 和 Medium 搜索预设。
- **Algorithm** 下拉菜单按移动端边缘检测的静态开销估算排列，依次为 Roberts Cross、Laplacian、Cross Max、Sobel、Scharr。Sobel 和 Scharr 同档。菜单使用枚举声明顺序，枚举数值对应已序列化的 Shader Pass 索引。

## 渲染顺序与数据格式

1. `DepthNormalOutlineFeature.cs` 先将描边层的不透明物体绘制到带独立深度缓冲的线性 RGBA32 RT。该 RT 使用点采样并关闭 MSAA，避免混合打包的字节。
2. `DepthNormalCapture.shader` 和 `DepthNormalPacking.hlsl` 将视空间单位法线用八面体编码写入 RG。线性视空间深度除以相机远裁面后，量化为 1 至 65535。B 存高字节，A 存低字节，编码 0 留给背景。读取端在同一 HLSL 文件中执行逆变换。
3. 开启描边反锯齿时，捕获阶段会留下目标的模板标记。边缘检测 Pass 复用该模板，在与深度法线 RT 相同的分辨率下输出覆盖率遮罩。优先使用线性 R8，缺少所需格式能力时回退至 RGBA8。
4. **Outline Objects (Original Materials)** 用原材质绘制目标，写入相机深度，并将可见样本的模板值设为 1。最终合成使用 `ZTest Always`、`ZWrite Off`，通过相机模板限制描边范围。开启 AA 时，只过滤描边覆盖率。相机模板覆盖到目标而遮罩中心落在背景、或中心紧邻背景时，滤波覆盖率取 1。Coverage 在硬折角保留中心覆盖率。FXAA 和 SMAA 1x 在最终的局部覆盖率混合中排除跨表面邻居。低分辨率重建还检查参与插值的对角纹素。关闭 AA 时直接采样深度法线并合成。
5. URP 随后绘制其余不透明物体及透明队列。普通不透明物体通过相机深度测试遮挡建筑及其线条。

边缘检测与 AA 中的背景采样来自描边组捕获 RT 的清空深度标记，或覆盖率遮罩上组外清空的 0。描边的 RGB 来自配置色，由硬件按覆盖率与通过 Stencil 的相机样本原色混合。开启 MSAA 时，最终 resolve 会将同一个像素里的物体样本和实际场景背景样本合成。

Low、Medium、High 分别按相机渲染宽高的 50%、75%、100% 分配深度法线 RT，最终合成在相机分辨率执行。采样半径在捕获阶段按质量比例换算为 RT 纹素。半径落在相邻整数纹素之间时，分别做点采样和边缘检测，再按小数部分插值覆盖率，两次检测共用中心样本。半径小于 1 个纹素时，使用 1 纹素半径检测，在最终混合时按比例缩放描边 alpha。

手游对比可从 Cross Max（四邻域最大差）和 Sobel 开始，再观察 Roberts Cross 的对角采样、Scharr 的斜边响应和 Laplacian 的二阶差分。Cross Max、Roberts Cross、Laplacian 每个检测片元读取深度法线 5 次，进行半径插值时读取 9 次。Sobel 和 Scharr 每个检测片元读取 9 次，进行半径插值时读取 17 次。

## 深度遮挡方案评估

- 当前内侧描边沿用相机 Stencil。原材质 Pass 写入真实表面深度，Stencil 标记目标的逐样本覆盖范围。后绘制的近处不透明物体通过常规深度测试同时遮挡目标及其描边。
- 若需要去掉相机 Stencil，可以考虑用原网格再画一次，保持相同的顶点变换、裁剪和深度偏移，以 `ZTest Equal`、`ZWrite Off` 合成。代价是额外的网格绘制。深度相等本身无法标记全屏 Pass 中的对象归属。
- 全屏描边顶点使用近裁面深度。要让全屏 Pass 参与深度测试，需要提供正确的设备深度，例如通过 `SV_Depth` 输出。现有打包深度是量化的线性深度，且捕获 RT 可能降分辨率，固定为单采样。用它匹配相机深度容易在斜面和 MSAA 边缘漏线。片元自写深度也可能让移动 GPU 改用较晚的深度测试。
- 外扩描边需要为轮廓外的覆盖样本指定深度。将其作为不透明表面时，写深度可使线条遮挡更远的物体。对需要平滑混合的外侧线条，可以在不透明场景绘制完成后依据场景深度合成。提前写深度会让部分覆盖的线条挡住后绘制的背景，可能出现光晕。

## 已验证的行为

2026-11-24 在 Unity 2021.3.45f2、Direct3D 11 下，以 480 × 270、4× MSAA 的示例主相机检查了五种算子、三档 RT 分辨率、四种 AA 模式共 60 种组合。每组的 1.5 像素渲染结果均与 1.0、2.0 像素有可测差异。固定深度法线输入的 GPU 测试中，五种算子的 1.0 和 2.0 纹素输出与旧 Shader 逐像素一致，1.5 纹素输出符合覆盖率插值。两种旧宽度字段名均通过迁移检查。浮点宽度改动的移动端 Shader 编译和真机 GPU 耗时待验证。

2026-11-22 通过编辑器主相机对照了关闭 AA、Coverage、FXAA 和 SMAA 1x。1280 × 720、4× MSAA、Sobel 下，三个 RT 质量档位均检查了目标外像素，FXAA 和 SMAA 1x 与关闭 AA 的对照均未检出差异。独立 GPU 用例检查了硬法线、深度台阶、Stencil 排除区域和低分辨率平坦区域。以 16 × 16 子采样计算斜线参考覆盖率，斜率 0.25 时 Coverage、FXAA、SMAA 1x 的边缘覆盖率均方根误差分别为 0.0877、0.0641、0.0538。其它斜率下排序有变化。11 个 Shader Pass 均通过 Android GLES3、Android Vulkan 和 iOS Metal 基础变体交叉编译。真机 GPU 耗时尚待测量。

2026-11-21 通过 Unity MCP 检查了两种算法与三档 RT 分辨率的全部六种组合。前景方块遮住建筑时，其内部像素在开关描边前后一致。测试物体移入 `OutlineObject` 后出现描边，移出后画面恢复。三套 URP 质量配置均通过 Play Mode 渲染检查，当次 Unity 控制台无错误。

同日扩展到五种算法后，使用编辑器主相机在 1280 × 720、3 像素半径、深度阈值 0.02、法线阈值 0.94 下实拍了全部 15 个组合。原有 Roberts Cross 和 Sobel 在三档分辨率下的画面均与修改前逐像素一致。捕获 Pass 和五个合成 Pass 均通过 Android GLES3、Android Vulkan 和 iOS Metal 的基础变体交叉编译。手机 GPU 耗时尚待真机测量。

加入反锯齿后验证了全部 15 种组合的开关效果，关闭时均与添加反锯齿前的画面逐像素一致。前景方块遮挡检查通过。捕获、五种边缘检测和覆盖率滤波共七个 Pass 均通过 Android GLES3、Android Vulkan 和 iOS Metal 基础变体交叉编译。

蓝底外轮廓回归采用 2560 × 1440、4× MSAA、Sobel、High 和 2 像素半径。对比同一帧关闭反锯齿的图像，2467 个建筑外边界像素中，任一 RGB 通道变亮超过 4 个 8 位色阶的像素由 1338 个降至 0 个。独立 GPU 用例覆盖了背景、深度台阶、硬法线折角和仅对角纹素跨面的低分辨率重建。修正后重新验证了 15 种算子与质量组合及三个移动图形后端的 Shader 编译。反锯齿现在额外读取深度法线作为滤波依据，真机耗时需重新测量。

2026-11-22 按匈牙利命名规范整理了工程源码。渲染器设置可通过 `m_eAlgorithm`、`m_eQuality`、`m_bAntiAliasing` 等字段访问，描边组的目标相机为 `_kTargetCamera`。重命名的序列化字段带有 `FormerlySerializedAs`，原有 Renderer 资产、场景相机引用和 Readme 内容均通过迁移检查。编辑器主相机在 640 × 360、4× MSAA 下复测了五种算子、三档质量和反锯齿开关。脚本热重载后立即抓取的前两帧曾与基准有差异，渲染稳定后连续两轮的 30 组画面均逐像素匹配整理前的结果。
