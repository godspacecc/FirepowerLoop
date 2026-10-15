---
name: ui-prefab-authoring
description: Use when creating, modifying, reviewing, or diagnosing AnotherDay UI Prefabs that contain lists, code-managed repeated elements, Box Prefabs, CustomListView, LoopScrollRect, or other custom collection components; also use when Controller code creates, destroys, pools, or binds repeated UI items.
---

# UI Prefab 编写

## 作用

保证每个由代码管理的重复界面元素，都能从宿主 UI Prefab 上直接找到实际使用的独立 Box Prefab。列表项的创建、复用和回收由选定的自定义组件负责，不能散落在 Controller 中。

使用本 Skill 时必须同时使用 `$unity-ui`。涉及手写运行时 C# 时，还必须使用 `$code-role`。

## 最终选择顺序

先判断是不是列表或其它重复元素集合：

```text
是不是列表或重复元素集合？
├─ 不是
│  └─ 按普通界面控件处理
└─ 是
   ├─ 内容完全固定，不由代码管理列表项
   │  └─ 所有内容直接制作在宿主 Prefab 中，只使用 Unity 原生布局组件
   └─ 列表项的数据、数量、创建、复用或移除由代码管理
      ├─ 先判断是同一种 Box 的显示状态，还是结构和职责不同的 Box 类型
      ├─ 每种实际需要复用的 Box 类型都建立独立正式 Box Prefab
      ├─ 宿主 Prefab 挂合适的自定义集合组件
      └─ 该组件直接引用外部 Box Prefab
```

“内容完全固定”说的是列表项由谁管理，不是数量是否固定。开始界面的按钮都直接制作在 Prefab 中，属于固定内容。背包即使永远只有十二格，只要代码会给格子绑定数据或管理格子，仍然属于动态列表。

动态列表按下面的顺序选择组件：

```text
动态列表选择
├─ 普通的小型有限列表，采用整体刷新
│  └─ CustomListView
└─ 需要对象池和可视区域显示优化的无限循环列表
   ├─ 纵向：LoopVerticalScrollRect
   ├─ 横向：LoopHorizontalScrollRect
   └─ 网格：LoopGridLayoutGroup
```

长列表或持续增长的列表通常需要 Loop 系列提供的优化，但这只是常见使用原因，不是组件定义。

同一个索引位置根据数据切换显示内容，不等于列表需要多种 Box。例如普通仓库格和尾部扩容入口尺寸相同、都占一个网格位置，区别只是显示“道具内容根”还是“扩容内容根”，应由同一个 Box Prefab 提供互斥状态。只有结构、尺寸、布局职责或复用生命周期确实不同，无法作为同一 Box 的完整状态重置时，才判定为多种 Box。

`CustomListView` 可以配合 `VerticalLayoutGroup`、`HorizontalLayoutGroup`、`GridLayoutGroup` 等 Unity 原生布局组件。原生布局组件只负责排列已经创建的 Box，不代替自定义集合组件。

当前 `CustomGridLayoutGroup` 会在刷新时销毁并重新创建全部子对象。在它的对象所有权和刷新行为被修正并形成明确规则之前，不新增使用。

## Prefab 必须满足的结构

每个动态列表都必须按以下顺序制作：

1. 每种可复用 Box 类型都建立为项目 UI Prefab 目录中的独立正式 Box Prefab；同一 Box 的互斥显示状态保存在该 Box Prefab 内。
2. 对 Box Prefab 执行 Box UI 生成工具，使其获得生成的 `BaseBoxUI` View 和有效的序列化引用。
3. 在宿主 UI Prefab 上挂选定的自定义集合组件。
4. 在 Inspector 中把外部 Box Prefab 赋给该组件的列表项来源字段。
5. Viewport、Mask、Content、布局方式、间距、单元尺寸和 Box Prefab 来源都保存在 Prefab 中。
6. Prefab 引用改变后，对宿主 Prefab 执行对应的 UI 生成工具。

检查宿主 Prefab 时，必须能够沿着自定义组件的序列化引用直接找到实际列表项 Prefab，不能靠搜索 Controller 代码才知道运行时创建了谁。

## Controller 职责

Controller 可以提供：

- 当前数据快照；
- 列表项数量；
- 索引对应的数据绑定；
- 显示内容和操作回调；
- 自定义组件已经公开的刷新、滚动和选择请求。

Controller 不得：

- 使用 `Instantiate`、`Destroy` 或通用对象池管理重复界面对象；
- 复制宿主 Prefab 中隐藏的 `SlotTemplate`、`ItemTemplate`、`TipTemplate` 等内嵌模板；
- 把复制到宿主 Prefab 内的组件或子对象当成可复用 Prefab 来源；
- 解包、复制或局部展开公共 Box Prefab，从而隐藏真实来源关系；
- 用运行时代码创建布局或修补层级，以弥补 Prefab 结构缺失。

`BaseBoxUI` 只是列表项生成出来的 View，不是列表管理组件，也不负责保存业务数据、事件注册、对象池状态或跨刷新生命周期状态。

## 现有组件无法满足时

开始实现前，必须先区分“同一 Box 的显示状态”和“不同 Box 类型”，再确认选定组件能表达全部实际 Box 类型和生命周期。

以下情况属于组件能力不足：

- 同一列表确实需要结构、尺寸、布局职责或复用生命周期不同的多种 Box Prefab，不能收敛为同一个 Box 的互斥状态；
- 每个列表项需要独立计时并单独回收；
- 列表需要可变尺寸、分组、吸顶、嵌套滚动等现有组件没有提供的行为；
- 名称上是多 Prefab 组件，但实际仍然只能指定一个 Prefab 来源。

处理顺序必须是：

```text
现有自定义组件不能表达需求
├─ 停止继续实现界面
├─ 明确报告缺少的组件能力
├─ 扩展合适的自定义组件，或新增专用自定义组件
└─ 宿主 Prefab 能直接引用全部正式 Box Prefab 后再继续
```

不得退回 Controller 动态创建、宿主内嵌模板或复制 Box 内容。

## 审查顺序

1. 检查实际宿主 Prefab、`ReferenceCollector`、生成 View、Controller、候选 Box Prefab 和自定义组件实现。
2. 按“最终选择顺序”判断内容属于固定内容还是动态列表。
3. 固定内容必须确认代码不会创建、删除、排序或绑定其中的列表项。
4. 动态列表先检查看似不同的项是否只是同一 Box 的互斥状态；确认属于不同 Box 类型后，才要求从宿主组件追踪多个外部 Box Prefab。
5. 搜索 Controller 和相关帮助类是否使用 `Instantiate`、`Destroy`、对象池或模板字段管理重复元素。
6. 检查复用时是否完整重设文字、图片、显隐、选中状态、可交互状态、数据标识、回调和拖拽状态。
7. 现有组件不能满足时，报告能力缺口，不能批准绕过规则的临时实现。

## 完成检查

- 固定内容直接制作在宿主 Prefab 中，没有列表项管理代码。
- 每种实际 Box 类型都有独立正式 Box Prefab；同一 Box 的互斥状态没有被误拆成多个 Box。
- 宿主 Prefab 上的自定义组件直接引用正确的 Box Prefab。
- 自定义组件符合已确认的选择顺序。
- Controller 和帮助类没有创建、销毁、池化或复制重复界面对象。
- 没有使用内嵌、解包、复制或局部展开的模板代替正式 Box Prefab 引用。
- Unity 反序列化后，生成 View 和序列化引用仍然有效。
- 复用列表项时完整覆盖上一次的显示状态和回调。
- 特殊需求先由合适的自定义组件提供能力，再继续制作具体界面。
