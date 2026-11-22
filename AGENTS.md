# Outline 工程记忆

本工程使用 Unity 2021.3.45f1c2 和 URP 12.1.15，已接入 Unity MCP。深度法线描边示例位于 `Assets/DepthNormal_PP/`。打开其中的 `DepthNormal.unity`，通过 **Game** 视图查看主相机效果。

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
- 在该资产的 **Depth Normal Outline** 中调整算法、质量、颜色、采样半径和阈值。当前默认值为 Sobel、High、2 像素采样半径、相对深度阈值 0.025、法线阈值 0.4。
- **AA Mode** 从上到下依次为 None、Coverage、FXAA、SMAA 1x，新建配置和示例 Renderer 均默认选 FXAA。None 直接合成原始描边，跳过 AA 覆盖率和 SMAA 中间 Pass。原先关闭 Anti Aliasing 的配置加载后会迁移为 None。
- Coverage 保留原有滤波。FXAA 在合成时沿遮罩边缘搜索，与 Coverage 的 Pass 数相同。SMAA 1x 在遮罩分辨率下增加边缘检测和混合权重两个 Pass，使用 URP 的 AreaTex、SearchTex 和 Medium 搜索预设。
- **Algorithm** 下拉菜单按移动端边缘检测的静态开销估算排列，依次为 Roberts Cross、Laplacian、Cross Max、Sobel、Scharr。Sobel 和 Scharr 同档。菜单使用枚举声明顺序，枚举数值对应已序列化的 Shader Pass 索引。

## 渲染顺序与数据格式

1. `DepthNormalOutlineFeature.cs` 先将描边层的不透明物体绘制到带独立深度缓冲的线性 RGBA32 RT。该 RT 使用点采样并关闭 MSAA，避免混合打包的字节。
2. `DepthNormalCapture.shader` 和 `DepthNormalPacking.hlsl` 将视空间单位法线用八面体编码写入 RG。线性视空间深度除以相机远裁面后，量化为 1 至 65535。B 存高字节，A 存低字节，编码 0 留给背景。读取端在同一 HLSL 文件中执行逆变换。
3. 开启描边反锯齿时，捕获阶段会留下目标的模板标记。边缘检测 Pass 复用该模板，在与深度法线 RT 相同的分辨率下输出覆盖率遮罩。优先使用线性 R8，缺少所需格式能力时回退至 RGBA8。
4. **Outline Objects (Original Materials)** 用原材质绘制目标，写入相机深度，并将可见样本的模板值设为 1。最终合成通过相机模板限制描边范围。开启 AA 时，只过滤描边覆盖率，再用配置的描边色和覆盖率混合到当前像素。相机模板覆盖到目标而遮罩中心落在背景、或中心紧邻背景时，保留完整描边。Coverage 在硬折角保留中心覆盖率。FXAA 和 SMAA 1x 排除来自另一表面的邻居，允许沿同一表面平滑。低分辨率重建还检查参与插值的对角纹素。关闭 AA 时直接采样深度法线并合成。
5. URP 随后绘制其余不透明物体及透明队列。普通不透明物体通过相机深度测试遮挡建筑及其线条。

手游对比可从 Cross Max（四邻域最大差）和 Sobel 开始，再观察 Roberts Cross 的对角采样、Scharr 的斜边响应和 Laplacian 的二阶差分。Cross Max、Roberts Cross、Laplacian 每个检测片元读取深度法线 5 次，Sobel 和 Scharr 各 9 次。Low、Medium、High 分别按相机渲染宽高的 50%、75%、100% 分配深度法线 RT，最终合成在相机分辨率执行。

## 已验证的行为

2026-11-22 通过编辑器主相机对照了关闭 AA、Coverage、FXAA 和 SMAA 1x。1280 × 720、4× MSAA、Sobel 下，三个 RT 质量档位均检查了目标外像素，FXAA 和 SMAA 1x 与关闭 AA 的对照均未检出差异。独立 GPU 用例检查了硬法线、深度台阶、Stencil 排除区域和低分辨率平坦区域。以 16 × 16 子采样计算斜线参考覆盖率，斜率 0.25 时 Coverage、FXAA、SMAA 1x 的边缘覆盖率均方根误差分别为 0.0877、0.0641、0.0538。其它斜率下排序有变化。11 个 Shader Pass 均通过 Android GLES3、Android Vulkan 和 iOS Metal 基础变体交叉编译。真机 GPU 耗时尚待测量。

2026-11-21 通过 Unity MCP 检查了两种算法与三档 RT 分辨率的全部六种组合。前景方块遮住建筑时，其内部像素在开关描边前后一致。测试物体移入 `OutlineObject` 后出现描边，移出后画面恢复。三套 URP 质量配置均通过 Play Mode 渲染检查，当次 Unity 控制台无错误。

同日扩展到五种算法后，使用编辑器主相机在 1280 × 720、3 像素半径、深度阈值 0.02、法线阈值 0.94 下实拍了全部 15 个组合。原有 Roberts Cross 和 Sobel 在三档分辨率下的画面均与修改前逐像素一致。捕获 Pass 和五个合成 Pass 均通过 Android GLES3、Android Vulkan 和 iOS Metal 的基础变体交叉编译。手机 GPU 耗时尚待真机测量。

加入反锯齿后验证了全部 15 种组合的开关效果，关闭时均与添加反锯齿前的画面逐像素一致。前景方块遮挡检查通过。捕获、五种边缘检测和覆盖率滤波共七个 Pass 均通过 Android GLES3、Android Vulkan 和 iOS Metal 基础变体交叉编译。

蓝底外轮廓回归采用 2560 × 1440、4× MSAA、Sobel、High 和 2 像素半径。对比同一帧关闭反锯齿的图像，2467 个建筑外边界像素中，任一 RGB 通道变亮超过 4 个 8 位色阶的像素由 1338 个降至 0 个。独立 GPU 用例覆盖了背景、深度台阶、硬法线折角和仅对角纹素跨面的低分辨率重建。修正后重新验证了 15 种算子与质量组合及三个移动图形后端的 Shader 编译。反锯齿现在额外读取深度法线作为滤波依据，真机耗时需重新测量。

2026-11-22 按匈牙利命名规范整理了工程源码。渲染器设置可通过 `m_eAlgorithm`、`m_eQuality`、`m_bAntiAliasing` 等字段访问，描边组的目标相机为 `_kTargetCamera`。重命名的序列化字段带有 `FormerlySerializedAs`，原有 Renderer 资产、场景相机引用和 Readme 内容均通过迁移检查。编辑器主相机在 640 × 360、4× MSAA 下复测了五种算子、三档质量和反锯齿开关。脚本热重载后立即抓取的前两帧曾与基准有差异，渲染稳定后连续两轮的 30 组画面均逐像素匹配整理前的结果。
