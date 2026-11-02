# Bootstrap 编辑器直接运行修复

问题：直接在编辑器运行入库的Bootstrap场景，显示`Startup setup is incomplete`及GameView的`No cameras rendering`。

根因：正式构建才会向生成场景注入NativeClientHost及原生身份/信任配置；入库场景没有Editor组合，ClientBootstrap.Start因协调器为空停止。原有原生主包验收没有覆盖这一源码开发入口。

修复全部位于Client.Editor程序集的PlayMode目录。场景加载后在ClientBootstrap.Start之前选择Editor本地组合，复用GF EditorResourceComponent、ResourceService、UIService和现有Client.Game.Entry，创建Editor相机并在真实首帧后隐藏启动界面。编辑器就绪不提交任何release健康状态，不运行原生更新流程，也不建立真实服务器会话。

生命周期通过Editor/scene事件管理；启动前检查GF资源和UI辅助器实际就绪。场景卸载、退出Play及脚本重载时取消并释放拥有的资源；清理静态GF注册表时只处理自己的旧宿主，避免影响替代场景。

## 验证

- [x] 真实加载入库Bootstrap的两条回归先复现原故障，2/2失败：`Builds/Tests/E1/20261102T010647295Z-1e2c5c35`。
- [x] 首次打开登录页、本地模拟交互、相机及重载后唯一页面/相机通过，2/2：`Builds/Tests/E1/20261102T011657678Z-d51ea9a6`。
- [x] 审查发现启动失败后退订了卸载回调但未清理GF注册表；真实无效Profile→失败→恢复Profile→重载的用例先RED（3项中1失败），修后GREEN3/3：`Builds/Tests/E1/20261102T012502727Z-5b1d29e8`、`20261102T012624436Z-8f4f0619`。
- [x] 完整EditMode413/413、图形PlayMode152/152、源码哈希前后不变：`Builds/Tests/EditorBootstrapFix/20261102T012745430Z/result.json`。
- [x] 小范围代码审查的1项Important已用上述RED→GREEN及全量套件关闭；普通编辑器实际打开Bootstrap并点击Play、停止后再次Play，两次均出现EDITOR_CLIENT_READY，画面无旧错误，Console警告/错误均0。原图与日志快照：`Builds/Tests/EditorBootstrapFix/20261102T012745430Z/manual-play-result.json`。

本次未修改Framework/Game运行时、正式Player信任链或已归档原生包；历史原生验收保留其原始范围。
