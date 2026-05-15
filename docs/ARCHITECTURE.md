# 《不完美的小店》工程架构说明

## 1. 架构目标

当前阶段优先完成可长期维护的 Unity 基建，而不是一次性写满玩法。基建要解决四件事：

- 文件放在哪里。
- 模块之间如何依赖。
- 数据如何配置和扩展。
- MVP 流程如何从空场景进入。

项目默认使用 Unity `2022.3.62f3`、2D、PC/Windows、uGUI 和 ScriptableObject。

## 2. 目录结构

业务资产统一放在 `Assets/_Project/` 下，避免散落在 Unity 默认目录中。

```text
Assets/
  _Project/
    Art/
    Audio/
    Prefabs/
    Scenes/
    ScriptableObjects/
      Customers/
      GameConfig/
      Materials/
      Orders/
      RepairMethods/
    Scripts/
      Core/
      Data/
      Flow/
      Gameplay/
      Save/
      UI/
      Utils/
    Tests/
    UI/
```

目录职责：

- `Scenes`：正式游戏场景。`SampleScene` 只保留为 Unity 默认参考，不作为正式入口。
- `Scripts`：所有项目 C# 脚本，按模块分层。
- `ScriptableObjects`：订单、材料、顾客、修补方式和初始配置资产。
- `Prefabs`：可复用 GameObject、UI 面板和流程入口 prefab。
- `UI`：uGUI 相关画布、样式、字体、图标占位资源。
- `Art` / `Audio`：美术和音频资产。
- `Tests`：后续 EditMode / PlayMode 测试。

## 3. 脚本分层

依赖方向应尽量单向：`UI → Gameplay/Flow → Data/Core`。底层模块不能反向依赖 UI。

- `Core`：通用枚举、基础值对象、游戏状态定义。
- `Data`：ScriptableObject 定义和只读配置数据。
- `Flow`：日循环、阶段切换、启动入口。
- `Gameplay`：订单处理、材料消耗、资源结算等玩法规则。
- `UI`：uGUI View、Presenter、面板绑定。
- `Save`：存档结构与读写入口，MVP 可先预留。
- `Utils`：项目级工具方法，避免放业务逻辑。

## 4. 数据配置

早期数据使用 ScriptableObject 管理，便于在 Inspector 中调试。

优先建立以下配置类型：

- `MaterialDefinition`：材料 ID、显示名、描述、默认价格。
- `CustomerDefinition`：顾客 ID、显示名、顾客类型、备注。
- `OrderDefinition`：订单 ID、物品类型、损坏程度、材料需求、能量消耗、报酬、反馈文本。
- `RepairMethodDefinition`：修补方式 ID、显示名、能量修正、收入修正、真实度修正。
- `GameInitialConfig`：初始金币、初始能量、初始真实度、每日能量恢复、初始材料库存。

规则：

- 配置类只保存数据，不写复杂业务流程。
- 运行时状态不直接写回配置资产。
- ID 使用稳定英文小写蛇形命名，例如 `wood`, `simple_cup_order`。

## 5. 流程架构

主流程用 `GamePhase` 表示当前阶段：

- `Boot`
- `DayStart`
- `OrderSelection`
- `Repair`
- `Delivery`
- `NightSummary`
- `MaterialPurchase`
- `DayEnd`

MVP 前先实现空流程入口：启动场景后进入 `Boot`，再切换到 `DayStart`。后续再逐步接入订单、修补和结算逻辑。

## 6. UI 架构

MVP UI 使用 uGUI，优先保证信息清楚，不追求最终美术。

建议面板：

- `ResourceBarView`：金币、能量、真实度、材料摘要。
- `OrderListView`：当天订单列表。
- `OrderDetailView`：订单详情和顾客备注。
- `RepairActionView`：修补按钮和失败提示。
- `NightSummaryView`：当日收入、能量、材料、反馈摘要。
- `MaterialPurchaseView`：简化材料购买入口。

UI 层只负责显示和转发玩家输入，不直接计算订单结果。

## 7. 命名规范

- C# 类型使用 `PascalCase`，字段使用 `camelCase`。
- 私有序列化字段使用 `[SerializeField] private`。
- ScriptableObject 菜单统一放在 `A Place Like Me/` 下。
- 场景命名使用 `S_` 前缀，例如 `S_Bootstrap.unity`。
- Prefab 命名使用 `PF_` 前缀，例如 `PF_GameBootstrap.prefab`。

## 8. 基建完成标准

- `Assets/_Project/` 目录完整。
- 架构文档、路线图、开发流程文档齐全。
- 数据模板脚本可编译。
- 至少有一个正式入口场景位于 `_Project/Scenes`。
- 后续 MVP 玩法可以在不重排目录和模块的前提下继续开发。
