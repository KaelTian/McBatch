# 项目约定(McBatch)

## 技术栈
- .NET 10, C# 12, xUnit
- 通信库: HslCommunication(三菱 MC 协议客户端 + MelsecMcServer 仿真服务器)
- 唯一可信的完成标准: `dotnet test` 全绿

## 当前状态
- v1.0(批量读)已完成: src/ 有实现, v1.0 测试已冻结, `dotnet test` 全绿
- SPEC 已升级到 v1.1: 新增 §8 批量写(含 §8.4 与验收标准 A8-A11)
- 当前任务: 按 SPEC §8 实现批量写
- 新测试只能放**新文件**, 经用户 review 并宣布冻结后才算完成

## 纪律(不可违反)

### tests/ 冻结规则
1. v1.0 既有测试文件: **永久冻结, 任何情况下禁止修改**
2. 新需求的测试: 只能新建文件; 用户 review 并宣布冻结后同样永久冻结
3. 测试失败 = 实现错了; 怀疑测试错了 → 在回复中向用户说明, 等用户裁决, 不许自己动手

### 工作方式
1. 动手前先读 `docs/SPEC.md`(含末尾修订记录), 以 SPEC 为唯一权威
2. 每次只实现 SPEC 的一个章节, `dotnet test` 全绿后再继续下一节
3. 发现 SPEC 有歧义或与实测冲突: 停止, 在回复中提问, 不要自行猜测
4. docs/SPEC.md 是用户的文件: 不要直接修改, 在回复中说明发现的问题
5. SPEC 中标注 ⚠️ 的"已知未知项"不要凭空推断, 在测试里留 TODO 并说明需要人工实测
6. 汇报必须包含: 测试运行结果、一次通过的测试清单、调试后通过的测试及每次修改的原因
7. 会话被 /clear 后: 重新读本文件和 SPEC.md 再动手

## 命令
```bash
dotnet test                      # 全量验证
dotnet test --filter "FullyQualifiedName~SchedulingProperties"   # 只跑 property 测试
```
