# 《不完美的小店》开发流程

## 1. 开发原则

- 先基建，再玩法，再表现。
- 先跑通一条垂直流程，再扩内容数量。
- 业务资产统一放入 `Assets/_Project/`。
- 不在 `Assets/Scenes/SampleScene.unity` 上继续做正式内容。
- 不为临时功能破坏脚本分层。
- 文档、脚本和 Unity 文本资产统一使用 UTF-8 编码。

## 2. Git 流程

- 每次改动前先确认 `git status`。
- 写入中文内容时使用支持 UTF-8 的编辑器或脚本，避免 PowerShell 默认编码造成乱码。
- 提交信息使用简短英文前缀，例如：
  - `docs: update roadmap`
  - `chore: add project folders`
  - `feat: add order data definitions`
  - `fix: correct material cost`
- 不提交 `Library/`、`Logs/`、`UserSettings/` 等本地生成目录。
- 文档和工程结构可同一提交；玩法逻辑建议独立提交。

## 3. Unity 资产规则

- 创建、移动、删除 Unity 资产时必须保留对应 `.meta` 文件。
- 正式场景放在 `Assets/_Project/Scenes/`。
- Prefab 放在 `Assets/_Project/Prefabs/`。
- ScriptableObject 资产按类型放在 `Assets/_Project/ScriptableObjects/` 子目录。
- 临时占位资源可以保留，但命名要带 `Placeholder` 或 `WIP`。

## 4. 脚本规则

- C# 命名空间统一使用 `APlaceLikeMe`。
- 数据定义放在 `APlaceLikeMe.Data`。
- 流程入口放在 `APlaceLikeMe.Flow`。
- 通用枚举和值对象放在 `APlaceLikeMe.Core`。
- UI 脚本放在 `APlaceLikeMe.UI`。
- 一个脚本只承担一个明确职责。

## 5. ScriptableObject 规则

- 配置资产只保存设计数据，不保存运行时状态。
- 所有配置对象都必须有稳定 ID。
- ID 使用英文小写蛇形命名。
- 显示名称可以使用中文。
- 后续如需外部表格导入，再从 ScriptableObject 迁移或增加导入层。

## 6. 场景与流程规则

- 正式入口场景命名为 `S_Bootstrap.unity`。
- 场景只负责挂载启动入口和全局引用，不直接堆玩法逻辑。
- 主流程阶段由 `GamePhase` 管理。
- Phase 0 只需要能进入空流程，不要求完整订单玩法。

## 7. 验证清单

基建改动完成后至少检查：

- `README.md` 能链接到所有核心文档。
- `Assets/_Project/` 目录结构完整。
- C# 脚本能被 Unity 编译。
- `ProjectSettings/ProjectVersion.txt` 未被意外改动。
- 没有临时提取文件或本地缓存进入 Git。
