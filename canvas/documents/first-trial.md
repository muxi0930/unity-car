# 光子狐飞车 · 十一城计时赛

驾驶均衡型赛车蓝隼，在十一城迷你版完成两圈。发卡弯前按住油门，以方向键和 Shift 起漂，反拉收车后用 W 短喷。漂移积攒氮气，空格释放。在短喷窗口内调整氮气和短喷的先后顺序可组成叠喷。圈内依次通过三个检查点，第二次有效冲线后显示总成绩和分圈成绩，回车再赛。

| 游戏素材 | 来源与用途 | Unity 资源 |
|---|---|---|
| 蓝隼 | 参考工程的 balanced.glb，保留车身零件及 0.65 模型缩放 | Assets/Art/BlueFalcon.foxmesh |
| 十一城迷你版 | 从原版城市、路面、护栏、起点和弯道提示网格导出 | Assets/Art/ElevenCity.foxmesh |
| 路线 | 1,200 个等弧长样点用于赛道边界、计圈及小地图 | Assets/Art/Route.json |
| 城市天空与沥青 | 原版天空全景图及原版路面纹理算法的输出 | Assets/Art/CitySky.jpg、Assets/Art/Asphalt.png |

场景入口为 Assets/Scenes/ElevenCity.unity。比赛使用 120 Hz 固定步长。跟车镜头以 64° 为基础垂直视野，喷射时拉宽。R 在当前路段回正，Esc 返回发车区。
