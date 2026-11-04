# 竖屏主城展示

设计画布为 1080×1920。主城通过 UIMainCity 全屏 RawImage 和独立正交相机渲染，保持旧资源的 2.5D 视觉和建筑相对位置。相机按竖屏视口等比裁切取景，地图铺满画面，任务等控件覆盖在地图上；顶部城主资源栏与底部功能栏暂不显示。

`CityViewport.fillViewport` 限制最远缩放，避免重新缩成画面中间的小地图；拖动范围随当前视野计算。支持鼠标拖拽、滚轮、单指拖拽和双指缩放；拖动手势不会触发建筑点击。点击建筑显示名称与本地详情，关闭窗口时释放相机、渲染纹理和临时场景。

资源位于 Client 对应分类目录：

- `Assets/Content/Prefabs/MainCity/MainCity.prefab`
- `Assets/Content/Models/MainCity/`
- `Assets/Content/Textures/MainCity/`
- `Assets/Content/Materials/MainCity/`
- `Assets/Content/Shaders/MainCity/`

资源迁移清单见 `migration/maincity-assets.json`。保留原资源 GUID，移除了旧客户端脚本和碰撞体，由新客户端建立点击代理。源客户端资源未修改。

Unity 菜单 `Game/MainCity/Prepare Resources` 检查资源，`Game/MainCity/Build Main City UI` 重建界面资产。登录入口先完成账号阶段，再点击“进入游戏”打开主城；返回登录会断开当前会话。

当前为本地可交互展示，建筑升级、资源结算、任务奖励和底部业务功能尚未接入服务器。新增的 CityViewport 属于 AOT 框架，正式发布需重新构建主包。
