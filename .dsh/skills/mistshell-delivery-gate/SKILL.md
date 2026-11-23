---
name: mistshell-delivery-gate
description: MistShell 项目专用交付门禁。用户未明确确认验收完成前，禁止提交代码（git commit/push）和打包；验收完成后才允许打包并提交代码。发布包统一放到 G:\shares\tool\mistShell.zip，文件名不带版本号。
---

# MistShell 交付门禁

本规则只适用于 `G:\tools\MistShell` 项目。

## 验收前（默认状态）

用户没有明确说出「验收完成」「验收通过」「可以提交」「可以打包」等确认前，视为验收未完成：

1. **禁止提交代码**：不要执行 `git commit`、`git push`，也不要创建提交或推送分支。
2. **禁止打包**：不要运行 `build.ps1`、`dotnet publish`、`Compress-Archive` 等生成发布包或 zip 的操作，也不要把包复制到共享目录。
3. 本地自检可以继续：`dotnet build`、单元测试、smoke tests、本地预览运行不属于“打包”，可以执行。
4. 不要自行判断验收完成；必须以用户当前对话中的明确确认作为依据。

## 验收完成后

用户明确确认验收完成后：

1. 先打包最新代码，发布包输出到 `G:\shares\tool\mistShell.zip`。
2. 包名固定为 `mistShell.zip`，**不带版本号**，直接覆盖旧包。
3. 确认打包成功、包内容完整后，再提交代码并按项目流程推送。

## 检查清单

- [ ] 用户已明确确认验收完成
- [ ] 最新代码已构建并通过测试
- [ ] `G:\shares\tool\mistShell.zip` 已更新
- [ ] 包文件名不含版本号
- [ ] 代码提交完成
