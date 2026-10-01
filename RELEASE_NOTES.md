# Herciniamihomo Pro v1.0.1 - Single-Instance Self-Healing & Fluid Cards Layout

Next-Generation 1,000Hz (1kHz) Extreme Physics & Liquid Glass Desktop Client for Mihomo (Clash.Meta) Core.

---

### 📦 资产下载 (Downloads)

| 文件名 | 类型 | 说明 |
| :--- | :--- | :--- |
| 💿 **[Herciniamihomo-Setup-x64.exe](https://github.com/Alex234123/Herciniamihomo/releases/download/v1.0.1/Herciniamihomo-Setup-x64.exe)** | **Windows 安装程序** (推荐) | 单文件向导安装包，自动释放核心并生成桌面/开始菜单快捷方式与控制面板卸载项 |
| 📦 **[Herciniamihomo-v1.0.1-windows-x64.zip](https://github.com/Alex234123/Herciniamihomo/releases/download/v1.0.1/Herciniamihomo-v1.0.1-windows-x64.zip)** | **便携免安装绿色版** | 解压即用，配置隔离，随身便携 U 盘首选 |

---

### ✨ v1.0.1 新特性与修复 (Changelog)

- 🛡️ **单实例唤醒与僵尸进程自愈重构 (Single-Instance Self-Healing)**
  - 彻底重构多开唤醒机制，改用 Windows API `EnumWindows` 精准枚举定位 WPF 窗口句柄（`HwndWrapper`）；
  - 注册 `ChangeWindowMessageFilter` / `ChangeWindowMessageFilterEx` 穿透 Windows 10/11 UIPI 权限隔离，托盘/隐藏状态秒级瞬时置顶激活；
  - 增加 300ms 进程存活熔断检测，若后台存在卡死无窗口僵尸进程自动回收接管，彻底杜绝双击无响应或无法打开的问题。

- 📐 **自适应卡片网格布局 (Fluid Responsive Uniform Grid)**
  - 重写节点列表排版引擎，引入 `ResponsiveCardsPanel` 自适应均分排版算法；
  - 彻底消除原有节点列表右侧多余留白，动态弹性伸缩铺满容器，两端始终与上方仪表盘构件 100% 严丝合缝平齐。

- 🖼️ **节点国家/地区图标常驻保活 (Node Icon Resilience)**
  - 优化节点图标的内存缓存策略与 UI 虚拟化回收判定，修复高频刷新或长时间运行过程中节点国旗图标消失的问题。

- ⚡ **延续 1,000Hz (1kHz) 极速物理引擎与 Liquid Glass 亚克力液态毛玻璃视觉。**

---

### 💻 运行要求
- **操作系统**：Windows 10 / Windows 11 (x64)
- **依赖运行库**：内置依赖已完全自包含，无缝运行于 Windows 默认自带的 .NET Framework 4.8
