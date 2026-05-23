# 制式改造 (ColonistModification)

RimWorld 1.6 模组——为殖民者批量标准化植入仿生部件。

## 功能

- **自定义改造模板**：创建手术模板，选择需要安装的植入物（仿生臂/腿/眼/脊椎等），支持基因植入
- **批量分配**：在概览页为多个殖民者分配同一模板
- **自动检测**：系统定期检查资源、医生、身体部位，条件满足时弹出确认窗或自动添加手术
- **部位分立追踪**：左臂/右臂、左眼/右眼各自独立，不会做完一侧就跳过另一侧
- **实时完成判定**：直接从殖民者身体检测已安装的植入物判断进度，无需额外记录
- **手术并发控制**：可限制同时进行的手术数量，防止医院爆满

## 开发

项目引用 RimWorld DLL，路径 `F:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\`。

```
dotnet build
```

输出：`publish/1.6/Assemblies/ColonistModification.dll`

推荐创建目录链接到 mod 目录：

```
mklink /D F:\SteamLibrary\steamapps\common\RimWorld\Mods\ColonistModification F:\RiderProjects\UnlimitedFoundations\ColonistModification\publish
```
