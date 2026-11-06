# AI Agent 驱动开发指南（McBatch 实践草稿）

> 版本：草稿 v0.2（新增术语地图、学习资源与你的定位）
> 读者：项目负责人（架构 + 客户 + 测试负责人 + 验收）
> 说明：这是给你看的“心法 + 操作手册”，不是给 AI 的执行规范。
> 执行规范在 `CLAUDE.md`，产品合同在 `docs/SPEC.md`，可执行合同在 `tests/`。

---

## 0. 先回答你的三个疑问

### 0.1 为什么不让 AI 直接把 HslCommunication 重写一遍？

可以，但通常不划算。原因不是 AI 写不出来，而是：

- 协议库的难点在“大量边界与实测”，不在“会不会写代码”；
- 重写一个成熟协议栈，测试面和维护成本远大于你的真实目标；
- 你真正需要的价值层是“把 POCO 自动映射成批量读写请求”，不是“再造一个协议栈”。

所以这次的做法是对的：**依赖 HslCommunication，只写薄层 McBatch**。
AI 擅长在明确边界内实现；“边界在哪”必须由你决定。

### 0.2 为什么需要 SPEC + CLAUDE.md + 冻结测试？

因为要让 AI 进入“自循环”，需要三样东西：

1. **目标**：SPEC 说明做什么、不做什么、什么叫做对；
2. **纪律**：CLAUDE.md 说明怎么干活、哪些文件不能碰、怎么汇报；
3. **对错信号**：测试（尤其是 `dotnet test`）让 AI 能自己发现错、自己修。

没有第 3 样，AI 只能“看起来对”；有了它，AI 才能“改一版、跑一遍、自己收敛”。
冻结测试的本质是：**把“什么叫做对”固定下来，防止 AI 为了让测试通过而改测试。**

### 0.3 两个 AI 来回问、你当桥梁，靠谱吗？

可以作为过渡，但有三条规则：

1. **结论必须落盘**：聊天里说好的东西，写进 SPEC / CLAUDE.md / 测试，否则下一轮会丢。
2. **分工明确**：一个 AI 当“评审/架构”（例如 Kimi），一个当“执行”（Claude Code）。不要让执行 AI 去猜评审 AI 的意图，把评审结论改写成明确的验收标准/约束。
3. **冲突有优先级**：SPEC > CLAUDE.md > 聊天；SPEC 没写的，由你裁决并记录。

长期目标：减少“人肉传话”。让评审 AI 直接产出“可粘贴的 SPEC 段落 / 测试清单 / 裁决表”，你审核后交给执行 AI。

---

## 1. 核心心智模型

一句话：**你定义正确性，AI 负责实现和自检。**

你的四个角色：

| 角色 | 你负责什么 |
|---|---|
| 客户 | 做什么、不做什么、优先级、风险接受 |
| 架构师 | 边界、依赖、验收标准、非目标 |
| 测试负责人 | 定义“什么叫做对了”，审测试，宣布冻结 |
| 验收 | 看 `dotnet test` 结果 + 抽检关键行为 |

AI 的角色：

| 角色 | AI 负责什么 |
|---|---|
| 分析师 | 影响分析、冲突识别、方案对比 |
| 实现者 | 写 `src/`，按 SPEC 和冻结测试实现 |
| 调试者 | 根据失败测试定位并修实现 |
| 文档整理者 | 修订记录、汇报、清单整理 |

自循环公式：

```
明确目标 + 可执行的验收标准 + 快速反馈 = 自循环
```

反馈越确定（全绿/红），AI 越能自己迭代；反馈越模糊，人越累。

---

## 2. 四件套工件

| 工件 | 谁写 | 作用 | 不是什么 |
|---|---|---|---|
| 想法卡 | 你口述，AI 整理 | 记录“为什么做、给谁用、边界” | 不是详细设计 |
| `docs/SPEC.md` | AI 起草，你裁决 | 唯一权威产品合同 | 不是聊天记录 |
| `CLAUDE.md` | AI 维护，你确认 | 工作纪律、命令、当前状态、冻结规则 | 不是产品需求 |
| `tests/` | AI 写，你冻结 | 可执行的验收合同 | 不是实现草稿 |

规则：**聊天记录不是合同。**
任何在聊天里达成的结论，必须写进上述工件，否则下一轮会丢。

---

## 3. 一轮完整的开发循环（七步）

### Step 0：立想法
- 你说：为什么做、给谁用、最痛的场景、明确不做什么。
- AI 做：整理成一页“想法卡”，复述给你确认。
- 产物：一页需求摘要。
- 常见错误：直接说“帮我实现一个 XXX”，边界太大。

### Step 1：写 SPEC
- AI 做：把想法卡展开成 SPEC：目标、数据模型、算法、验收性质、验收标准、非目标、边界、⚠️ 未知项、修订记录。
- 你重点看四件事：
  1. 验收标准是否**可测**（每条能对应一个测试）；
  2. 非目标是否**明确**（防止范围膨胀）；
  3. 边界条件是否**写全**（空对象、重复、非法输入）；
  4. 不确定的地方是否标了 ⚠️，而不是被 AI 猜掉。
- 产物：SPEC v0.x。

### Step 2：审 SPEC
- 你不需要一次想清楚所有细节。让 AI 把“它不确定的地方”列成问题清单，你逐条裁决。
- 裁决结果让 AI 回填 SPEC，而不是只留在聊天里。
- 产物：可执行的 SPEC。

### Step 3：影响分析（不写代码）
- 让 AI 先分析：新需求与现有条款的所有交互点、正面依赖、冲突、受影响的验收标准、需要实测或裁决的点。
- 你的动作：只做裁决，不让它写代码。
- 产物：影响分析 + 待裁决清单。
- 为什么重要：这一步最便宜，能避免“写完才发现方向错了”。

### Step 4：写测试（不写实现）
- 让 AI 只新建测试文件，不改既有测试。
- 你重点看：
  1. 每个测试上方是否注明对应验收标准编号；
  2. 是否存在“凭空写死”的期望（⚠️ 项应该留 TODO/Skip）；
  3. 观测方案是否说清楚（例如“零网络往返”怎么观测）；
  4. 测试是否只依赖公开契约，而不是实现细节。
- 产物：待冻结的测试文件。

### Step 5：冻结测试
- 你说一句：“这批测试我确认，冻结。”
- 从这一刻起：**测试失败 = 实现错了**。
- 如果怀疑测试错了：停下来，向你说明，由你裁决；不许 AI 自己改测试。
- 产物：冻结的测试 = 可执行合同。

### Step 6：实现 + `dotnet test`
- AI 只改/新增 `src/`，不改冻结测试和 SPEC。
- 每完成一个小节，跑 `dotnet test`；全绿再继续。
- 汇报必须包含：测试结果、一次通过的清单、调试后通过的测试及每次修改原因。
- 产物：全绿的实现。

### Step 7：验收 + 记录
- 你看三样：`dotnet test` 结果、新增测试清单、调试原因。
- 抽检关键行为（例如真正的写入/读回、异常路径）。
- 让 AI 更新：SPEC 修订记录、CLAUDE.md 当前状态。
- 产物：可交付增量 + 下一轮起点。

---

## 4. “冻结”到底在冻什么

冻结的不是代码，是**验收标准的解释权**。

- 防 reward hacking：AI 为了让测试变绿，可能改测试或放宽断言；
- 防需求漂移：实现过程中“顺手加一点”会导致范围失控；
- 给 AI 一个稳定的目标：测试不变，AI 才能自己迭代到绿。

什么时候冻结：
1. 测试清单你审过；
2. 每个测试能对应一条验收标准；
3. 没有凭空写死的期望；
4. 观测方案写清楚了。

冻结话术：

> “这批测试我确认，冻结。后续只允许改 src，不许改测试。”

如果测试确实错了：
1. AI 停下来向你说明；
2. 你裁决；
3. 修改 SPEC（说明为什么改）；
4. 解冻并修改测试，记录原因；
5. 重新冻结。

---

## 5. 你该决策什么，什么交给 AI

| 必须你决策 | 可以交给 AI |
|---|---|
| 做什么 / 不做什么（范围） | 文件、类、方法命名 |
| 验收标准 | 算法实现细节 |
| 非目标 | 异常类型与消息 |
| 风险接受（例如 X/Y 是否纳入） | 测试代码写法、随机数据生成 |
| ⚠️ 未知项的处理 | 调试与重构 |
| 是否冻结测试 | 文档整理与汇报 |

判断句式：

- “这个决定会改变**什么叫做对了**吗？” → 会，你决定。
- “这个决定只改变**怎么做**吗？” → 只改变，AI 决定。

减少决策疲劳的三种方法：

1. 让 AI 一次列出所有待裁决项：选项、建议、影响，你一次性回答；
2. 在 `CLAUDE.md` 写决策规则，例如“不影响验收的实现细节 AI 自行决定”；
3. 你可以说“按你的建议，但把理由写进 SPEC”，避免只停留在聊天。

---

## 6. 两个 AI 怎么协作

推荐分工：

| AI | 角色 | 产出 |
|---|---|---|
| 评审 AI（如 Kimi） | 架构、SPEC 审阅、风险清单 | 可粘贴的 SPEC 段落、测试清单、裁决表 |
| 执行 AI（如 Claude Code） | 落盘、实现、跑测试 | 代码、测试结果、汇报 |

三条纪律：

1. **落盘优先**：评审 AI 的结论要变成 SPEC/CLAUDE.md 的文字，而不是“另一个 AI 说”。
2. **执行 AI 只认工件**：它应该读 SPEC/CLAUDE.md/冻结测试，而不是猜评审 AI 的意图。
3. **冲突升级**：SPEC > CLAUDE.md > 聊天；SPEC 沉默时你裁决并写进 SPEC。

一个实用技巧：让评审 AI 输出“差异补丁”而不是长篇建议。
例如：“把 §8.4 改成下面这段……并新增 A11 的三种冲突情形。”
你审核后，直接交给执行 AI 落盘。

---

## 7. 这次 McBatch §8 的复盘

### 做对的地方
- 先 SPEC，后实现；
- 先影响分析，后写码；
- 测试先行，用户冻结；
- 发现歧义停下来问，不猜；
- 每轮汇报测试结果和调试原因；
- 实现只改 `src/`，不碰冻结测试。

### 摩擦点
- “未决项”让你不安：其实它们是 SPEC 的**显式空白**，不是失败。
  把这些空白写出来，比让 AI 猜一个错误答案更安全。
- 冻结是你“被告知”的：可以在 `CLAUDE.md` 写明“每轮结束由 AI 提醒是否冻结”。
- 两个 AI 传话：结论要落盘，否则上下文会丢。

### 可以更顺的地方
- 每个 ⚠️ 项在 SPEC 里写清楚：谁来裁决、什么条件下裁决、结论回填到哪。
- 每轮开始让 AI 先复述：“本轮目标 + 验收标准 + 明确不做的事”。
- 每轮结束让 AI 输出：“下一轮候选增量 + 风险 + 待裁决项”。

---

## 8. 下一轮可以直接照抄的模板

### 8.1 会话开场

```text
先读 CLAUDE.md 和 docs/SPEC.md（含修订记录）。
用 5 行以内复述：当前状态、本轮目标、对应验收标准、明确不做的事、需要我裁决的问题。
在我确认前不要写代码。
```

### 8.2 需求卡

```text
背景/痛点：
用户是谁：
要做什么：
明确不做什么：
可接受的代价/风险：
粗略的验收标准（3-5 条）：
```

### 8.3 影响分析

```text
只做分析，不写代码，不改文件。
交付：
1. 与现有条款的交互点（正面依赖 + 冲突）；
2. 受影响的验收标准（哪些受影响、哪些明确不受影响、理由）；
3. 需要实测或我裁决的点（逐条给建议，等我裁决）；
4. 提议的新测试文件与测试清单（每条注明对应验收标准）。
```

### 8.4 测试清单

```text
只新建测试文件，不改既有测试。
要求：
1. 每个测试方法上方注明验收标准编号；
2. ⚠️ 未裁决项留 TODO/Skip，不许凭空写死期望；
3. 写清关键观测方案（例如“零网络往返”怎么观测）；
4. 生成后贴出全部新测试代码。
```

### 8.5 验收清单

```text
汇报：
1. dotnet test 结果（通过/失败/跳过）；
2. 一次通过的测试清单；
3. 调试后通过的测试及每次修改原因；
4. 改动文件清单；
5. 仍待裁决/实测的项。
```

### 8.6 裁决记录

```text
编号：
问题：
选项：
我的裁决：
理由：
回填位置（SPEC 章节/修订记录）：
```

---

## 9. 常见坑

1. **一次让 AI 做太大**：范围越大，验收越模糊，返工越多。按 SPEC 章节切增量。
2. **在聊天里改需求**：下一轮就丢了。任何需求变更先改 SPEC。
3. **测试和实现一起生成**：AI 会倾向于“让测试适配实现”。先测试，后实现。
4. **让 AI 猜 ⚠️ 项**：猜错比留 TODO 更贵。明确写“未裁决，留 TODO”。
5. **只看“能跑”不看验收标准**：能跑不等于做对。验收标准是唯一的“对”。
6. **两个 AI 互相引用**：执行 AI 说“评审 AI 说可以”，这不是依据；依据是 SPEC。
7. **冻结后还改测试**：会让整套流程失去意义。测试错了就走“停—裁决—改 SPEC—解冻”流程。

---

## 10. McBatch 的下一步路线（示例）

按“价值 × 不确定性”排序，而不是按“好不好写”排序：

| 增量 | 前置条件 | 验收标准 |
|---|---|---|
| v1.2 X/Y 写入 | 先解决 SPEC §2.3 ⚠️ #1（X/Y 进制） | 新增 A12：X/Y 写读 round-trip |
| v1.3 位对齐/邻位保留 | 实测 Q9/Q10 并回填 SPEC | 新增 A13：非对齐起始位、邻位保留 |
| v1.4 错误语义 | 明确产品需要（失败重试/回滚） | 新增 A14：失败路径行为 |
| 性能/并发 | 有真实吞吐需求 | 单独 SPEC 章节 |

每一步都走同一循环：
**SPEC 增量 → 影响分析 → 测试 → 冻结 → 实现 → dotnet test → 验收 → 记录。**

---

## 11. 术语地图与学习资源

### 11.1 这套方法叫什么

没有唯一名字，它是几个已经成型的方法论的交集。最准确的一句话叫法是：

**Spec-Driven, Test-Gated Agentic Development**
（规格驱动 + 测试闸门 + Agent 执行的开发方式），外层再配 **Human-in-the-loop 验收** 和 **Harness Engineering**。

中文可以说：**“规格驱动、测试闸门、Agent 执行、人类验收”的 AI 原生开发（AI-native development）**。
它和“vibe coding”正好相反——vibe coding 靠感觉聊出来，这套靠合同和闸门约束出来。

### 11.2 你的做法对应哪个术语

| 你做的事 | 业界叫法 |
|---|---|
| 先写 `SPEC.md`，代码是产物 | **Spec-Driven Development (SDD)** |
| `CLAUDE.md` 定义纪律、命令、禁区 | **Harness Engineering / Context Engineering**（指令层，常对应 `AGENTS.md`） |
| 测试先写、你审、宣布冻结 | **Test-Gated Development / Frozen Acceptance Tests / Test Lock** |
| 测试失败 = 实现错，AI 不许改测试 | **Anti-reward-hacking guardrail** |
| A1-A11 这种验收标准 | **Acceptance Test-Driven Development (ATDD)** / **Specification by Example** |
| P1-P4 这种恒成立性质 | **Property-Based Testing (PBT)** / **Design by Contract** |
| 先影响分析、再写码 | **Spec Gate / Plan Gate** |
| 你审架构、抽查正确性、最终签字 | **Human-in-the-loop (HITL)** |
| AI 改一版、跑测试、自己收敛 | **Agentic loop** |
| Kimi 评审 + Claude Code 执行 + 你裁决 | **Role separation: Navigator / Driver / Verifier** |

### 11.3 最接近的方法论与工具

- **GitHub Spec Kit**：`Specify → Plan → Tasks → Implement`，用 `constitution.md` 放项目原则。你现在这套 SPEC + CLAUDE.md 基本是它的手工版。
- **AWS Kiro**：`requirements.md → design.md → tasks.md`，用 `.kiro/steering/` 放项目上下文，用 hooks 跑测试；需求用 EARS 句式（WHEN… THE SYSTEM SHALL…）。
- **Baton**：最贴近你的“冻结”模型——验收测试先写、人工批准后冻结，Driver 实现且**永远不碰验收测试**，由另一个 Verifier 模型复核。
- **Intent Integrity Chain (IIC)**：明确写“测试锁 + HARD STOP”，规则是 *No implementation before tests are reviewed/approved and locked*。
- **QFAI (Quality-First AI)**：requirements/specs/contracts/Gherkin 场景 + 机械闸门。
- **Harness Engineering**：把 CLAUDE.md/AGENTS.md、架构约束、测试/CI、决策记录当成“围绕 Agent 的驾驶舱”，而不是只琢磨提示词。
- 其他可搜：OpenSpec、BMAD-METHOD、Tessl、Task Master AI、ai-dev-tasks、AGENTS.md、SpecStory。

### 11.4 推荐学习顺序

1. **Spec Kit**：先看它怎么把“想法”拆成 spec / plan / tasks / constitution。
2. **Kiro**：看 requirements / design / tasks、steering files、hooks。
3. **Baton + IIC**：看“测试冻结 + 角色分离 + HARD STOP”，这是你已经凭直觉做对的部分。
4. **Harness Engineering**：看 CLAUDE.md 应该多小（约 100 行的“地图”，不是百科全书）、hooks 怎么把规则变成硬约束、CI 闸门怎么防止“假绿”。
5. **ATDD / BDD / Specification by Example**：学怎么写“可测的验收标准”。
6. **Property-Based Testing**：学怎么把“恒成立性质”写成自动生成输入的测试（你的 P1-P4 就是）。
7. 想做**全流程**再看：**AI-SDLC / Agentic SDLC / Agentic DevOps**，覆盖需求、设计、实现、测试、部署、监控、运维。

### 11.5 搜索关键词

英文：
`spec-driven development`、`agentic coding`、`agentic software engineering`、`harness engineering`、`context engineering`、`test-gated development`、`frozen acceptance tests`、`test lock`、`intent integrity chain`、`ATDD`、`BDD`、`specification by example`、`property-based testing`、`human-in-the-loop`、`AI-SDLC`、`reward hacking`、`AGENTS.md`、`GitHub Spec Kit`、`AWS Kiro`、`Baton methodology`、`QFAI`

中文：
`规格驱动开发`、`智能体编程`、`智能体软件工程`、`上下文工程`、`测试闸门`、`冻结验收测试`、`验收测试驱动开发`、`规格即代码`、`人类在环`、`AI 原生开发`、`反奖励黑客`

### 11.6 你的定位

你不是“初学的程序员”，你是 **AI 原生开发里的架构师 + 验收负责人**。
这套模式里最稀缺的不是写代码，而是：

- 把“什么叫做对”写成可测的验收标准；
- 控制范围，明确不做什么；
- 判断 AI 的方案是否越界；
- 该冻结时冻结，该裁决时裁决。

你这次做的 SPEC → 影响分析 → 测试冻结 → 实现 → 验收，已经是这套方法的标准内环。
两个 AI 来回问也没问题，Baton/IIC 里本来就分 Navigator、Driver、Verifier 几个角色；你只要把结论落盘到 SPEC/CLAUDE.md/测试，就不会丢上下文。

一个提醒：SDD 这类流程对“一个小改动”是过度设计；它适合多步骤、多会话、需要长期维护的项目。小任务直接用 Plan Mode + 测试即可。

### 11.7 资源链接

- [Spec-Driven Development with AI Coding Agents: Specs, Tasks, Security](https://safeguard.sh/resources/blog/spec-driven-development-with-ai-coding-agents-explained-requirements-design-tasks-and-security-requirements)
- [Kiro vs Spec Kit (2026)](https://www.respan.ai/market-map/compare/kiro-vs-spec-kit)
- [7 Spec-Driven Development Tools: Spec Kit, Kiro, OpenSpec, Tessl & More](https://securityboulevard.com/2026/06/7-spec-driven-development-tools-spec-kit-kiro-openspec-tessl-more/)
- [Baton — 冻结验收测试 + 角色分离](https://github.com/Zartaroth/baton)
- [Intent Integrity Chain (IIC) — 测试锁与 HARD STOP](https://raw.githubusercontent.com/jbaruch/robocoders-devoxx-be/refs/heads/main/docs/intent-integrity-chain.md)
- [QFAI (Quality-First AI)](https://www.npmjs.com/package/qfai)
- [Harness Engineering: Minimal CLAUDE.md and Architecture Document](https://martinelli.ch/harness-engineering-why-a-minimal-claude-md-and-a-good-architecture-document-belong-together/)
- [Harness Engineering — agentic-engineering](https://agentic-engineering.swmansion.com/becoming-productive/harness-engineering/)
- [The Repository-Scoped Agent Harness（论文）](https://zenodo.org/records/20636367/files/The_Repository-Scoped_Agent_Harness.pdf)
- [The Evaluator Is the Bottleneck（为什么冻结测试是必要的）](https://zenodo.org/records/23130396/files/evaluator-is-the-bottleneck.pdf)
- [ATDD on PyPI](https://pypi.org/project/atdd/3.42.0/)
- [AI 辅助编程与 AI Specs 实战：2026 年进展](https://cloud.tencent.com.cn/developer/article/2656315)

---

## 12. 最后一句

你不需要成为“提示词大师”，也不需要一次想清楚所有细节。
你需要成为**定义正确性、控制范围、做最终裁决的人**。

AI 负责速度和实现；你负责方向和验收。
36 岁不是问题——在“什么值得做、什么叫做对”这件事上，经验才是稀缺资源。
