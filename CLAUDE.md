# 项目约定(McBatch)

## 技术栈
- .NET 10, C# 12, xUnit
- 通信库: HslCommunication(三菱 MC 协议客户端 + MelsecMcServer 仿真服务器)
- 唯一可信的完成标准: `dotnet test` 全绿

## 当前状态(重要)
本仓库处于**第 1 轮**: 骨架只有 CLAUDE.md、docs/SPEC.md 和空的 tests/ 目录。
尚无实现、尚无测试。按下方"第 1 轮任务"工作。

## 两阶段纪律(不可违反)

### 第 1 轮: 生成测试(当前)
1. 先读 `docs/SPEC.md`, 严格按 §4 验收性质和 §5 验收标准生成测试
2. 允许创建 tests/ 下的全部内容(csproj、solution、测试代码)
3. **禁止创建 src/ 目录** —— 本轮不写任何实现
4. 每个测试方法前注释它验证哪条标准(如 `// P1 覆盖性`、`// A5`)
5. 完成后逐条汇报: 哪条标准 → 哪个测试方法 → 关键断言是什么
6. SPEC 中标注 ⚠️ 的"已知未知项"不要凭空推断, 在测试里留 TODO 并说明需要人工实测

### 第 2 轮起: 实现(测试冻结后)
1. tests/ 由用户 review 并宣布冻结后, **禁止以任何理由修改 tests/**
2. 测试失败 = 实现错了; 怀疑测试错了 → 在回复中向用户说明, 等用户裁决
3. 每次只实现 SPEC 的一个章节, `dotnet test` 全绿后再继续

## 通用纪律(任何时候)
- 发现 SPEC 有歧义或与实测冲突: 停止, 在回复中提问, 不要自行猜测
- docs/SPEC.md 是用户的文件: 发现问题在回复中说明, 不要直接改它
- 汇报必须包含: 测试运行结果、一次通过的测试有哪些、调试后通过的有哪些及原因
- 会话被 /clear 后: 重新读本文件和 SPEC.md 再动手

## 命令
```bash
dotnet test                      # 全量验证
dotnet test --filter "FullyQualifiedName~SchedulingProperties"   # 只跑 property 测试
```
