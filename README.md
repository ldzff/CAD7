# RobTeach

Robot teaching

## 1. 引言

本报告为上位机换型软件的整体设计、技术路线和实现方式提供详细方案。该软件旨在支持不同产品换型时，通过解析CAD图纸生成机械臂喷淋轨迹，允许用户选择轨迹、设置喷嘴和喷淋类型，保存配置，并通过Modbus协议与机械臂通信。以下内容涵盖需求分析、系统架构、技术选型、模块设计、实现步骤和注意事项。

## 2. 需求分析

根据用户需求，软件需实现以下功能：

- **CAD图纸解析**：支持DXF格式（使用 IxMilia.Dxf 库），提取几何实体（线段、圆弧、圆、多段线等）。
- **轨迹生成**：将选定CAD实体转换为机械臂运动轨迹。轨迹基于基元类型（线、圆弧、圆）及其几何参数（如起点、终点、圆心、半径、三点定义）生成。
- **喷涂遍管理（Spray Pass Management）**：
    - 允许用户创建、命名和管理多个喷涂遍（Spray Pass）。
    - 每个喷涂遍包含独立的轨迹序列。
    - 用户可以切换当前活动的喷涂遍。
- **用户交互与轨迹编辑**：
    - 提供直观界面显示CAD图纸，支持缩放、平移。
    - 支持通过点击或框选（Marquee Selection）方式选择CAD实体，并将其添加到当前喷涂遍的轨迹列表中。按住Shift键框选可取消选择区域内的实体。
    - 允许用户调整所选轨迹在当前喷涂遍中的顺序。
    - 提供详细的轨迹参数设置：
        - **喷嘴控制**：为每条轨迹独立配置上、下喷嘴的启用状态，以及各自的气阀和液阀的开关状态。
        - **方向控制**：允许反转线段和圆弧类轨迹的默认加工方向。
        - **Z轴坐标调整**：为线段的起点/终点，圆弧/圆的特征点（通过三点定义）提供Z轴坐标的调整功能。
        - **加工时间（Runtime）**：允许用户编辑每条轨迹的预期加工时间，用于计算速度。
- **配置管理**：
    - 保存配置到本地JSON文件，支持加载和修改。
    - 配置中直接嵌入DXF文件内容，确保项目移植性。
    - 保存画布视图状态（缩放/平移级别）。
- **Modbus通信与测试**：
    - 将当前选定喷涂遍的配置数据发送到机械臂控制器。
    - 发送前检查机器人状态（通过读取特定Modbus寄存器，如1000号：0=忙碌, 1=就绪, 2=错误）。
    - 提供“测试运行”功能，允许用户选择慢速（值11写入1001号寄存器）或标准速度（值22写入1001号寄存器）模式，并通过写入特定值（如33到1002号寄存器）启动机器人空运行。
- **输出数据文件**：生成一个详细的文本文件（`RobTeach_SendData_*.txt`），包含所有喷涂遍及其轨迹的加工参数，供机器人直接使用或进一步处理。

### 2.1 假设与约束

- CAD图纸主要为2D DXF格式。虽然支持Z轴调整，但轨迹生成和大部分交互仍基于2D平面投影。
- 机械臂控制器支持Modbus TCP协议，具体寄存器映射在软件中有预定义。
- 软件运行于Windows平台，使用WPF框架。

## 3. 系统架构

软件采用模块化架构，分为以下核心模块：

- **CAD解析模块**：负责加载和解析DXF文件，提取几何实体。
- **轨迹生成模块**：根据基元（线段、圆、圆弧），用起点、终点、半径、角度等参数生成轨迹。
- **用户界面模块**：提供WPF界面，显示CAD图纸、支持交互操作。
- **配置管理模块**：处理轨迹和参数的保存与加载。
- **Modbus通信模块**：通过Modbus协议与机械臂控制器通信。

### 架构图

| 模块       | 功能描述                            | 依赖库/技术       |
| ---------- | ----------------------------------- | ----------------- |
| CAD解析    | 解析DXF文件，提取线段、圆弧等实体   | IxMilia.Dxf       |
| 轨迹生成   | 将实体转换为机械臂轨迹信息          | 自定义算法        |
| 用户界面   | 显示CAD图纸，支持轨迹选择和参数设置 | WPF               |
| 配置管理   | 保存和加载JSON配置                  | JSON.NET          |
| Modbus通信 | 发送轨迹数据到机械臂控制器          | EasyModbusTCP.NET |

## 4. 技术选型

### 4.1 编程语言与框架

- **C#**：成熟的Windows开发语言，支持丰富的库和工具，适合快速开发。
- **WPF**：提供矢量图形渲染，适合显示CAD图纸，支持复杂的用户交互。

### 4.2 CAD解析

- **IxMilia.Dxf**（[IxMilia.Dxf GitHub](https://github.com/ixmilia/dxf)）：一个 .NET Standard 库，用于读取和写入 DXF 文件。它支持多种 DXF 实体和版本。
- 理由：DXF是工业标准格式，IxMilia.Dxf 是一个积极维护的库，支持 .NET Standard，使其具有良好的跨平台兼容性和现代 .NET 项目的适用性。

### 4.3 轨迹生成

- **基元定义与参数化**：
  - CAD实体（如DXF的Line, Arc, Circle）被选中后，会转换为程序内部的 `Trajectory` 对象。
  - **直线 (Line)**：由起点和终点定义（包含X,Y,Z坐标）。
  - **圆弧 (Arc)**：通过三个点（起点P1, 弧上点P2, 终点P3）进行定义。每个点包含X,Y,Z坐标及可选的Rx,Ry,Rz姿态角。程序内部通过这三点计算出圆心、半径、起始/结束角度。
  - **圆 (Circle)**：同样通过三个点（P1, P2, P3）进行定义，以确定圆的平面、圆心和半径。每个点包含X,Y,Z坐标及可选的Rx,Ry,Rz姿态角。
  - **多段线 (LwPolyline)**：分解为直线和弧段序列。
- **点序列生成 (Discretization)**：
  - 对于圆弧和圆类型的轨迹，其定义的几何参数（如圆心、半径、起止角）会用于生成一系列离散点，以供显示和机器人路径执行。离散化的分辨率（如 `TrajectoryPointResolutionAngle`）用于控制点的密度。
  - 直线轨迹的点序列即为其起点和终点。
- **轨迹属性**：
  - 每条轨迹关联详细的喷嘴控制参数、加工方向（可反转）、Z轴高度、加工时间（Runtime）等。
- **坐标变换**：画布支持缩放和平移。实际坐标变换以匹配机械臂工作空间主要通过确保CAD图纸原点和尺寸与机器人工作区对齐，以及通过Z轴调整实现。全局变换参数（如 `Transform` 类）不再是主要的配置项，重点在于点本身的坐标。

### 4.4 用户界面

- 使用WPF的Canvas和Shape类绘制CAD实体。
- 支持交互功能：缩放、平移、实体选择、参数设置。
- 提供轨迹预览，确保用户确认轨迹正确性。

### 4.5 配置管理

- 使用JSON.NET序列化轨迹数据和参数。

- 数据结构示例：

  ```csharp
  // Represents a point with X, Y, Z coordinates and Rx, Ry, Rz orientation angles
  public class TrajectoryPointWithAngles {
      public DxfPoint Coordinates { get; set; } // X, Y, Z from DxfPoint
      public double Rx { get; set; } // Rotation around X-axis
      public double Ry { get; set; } // Rotation around Y-axis
      public double Rz { get; set; } // Rotation around Z-axis
  }

  public class Trajectory {
      // public List<Point> Points { get; set; } // (UI-only, not directly in JSON, generated from geometric params)
      public string PrimitiveType { get; set; } // "Line", "Arc", "Circle"

      // Geometric parameters for Line
      public DxfPoint LineStartPoint { get; set; }
      public DxfPoint LineEndPoint { get; set; }

      // Geometric parameters for Arc (3-point definition)
      public TrajectoryPointWithAngles ArcPoint1 { get; set; }
      public TrajectoryPointWithAngles ArcPoint2 { get; set; } // Midpoint on arc
      public TrajectoryPointWithAngles ArcPoint3 { get; set; }

      // Geometric parameters for Circle (3-point definition)
      public TrajectoryPointWithAngles CirclePoint1 { get; set; }
      public TrajectoryPointWithAngles CirclePoint2 { get; set; }
      public TrajectoryPointWithAngles CirclePoint3 { get; set; }
      // Original DxfEntity handle/info might also be stored for reconciliation

      public bool IsReversed { get; set; }       // Trajectory direction reversal
      public double Runtime { get; set; }        // Trajectory execution time in seconds

      // Detailed Nozzle Control
      public bool UpperNozzleEnabled { get; set; }
      public bool UpperNozzleGasOn { get; set; }
      public bool UpperNozzleLiquidOn { get; set; }
      public bool LowerNozzleEnabled { get; set; }
      public bool LowerNozzleGasOn { get; set; }
      public bool LowerNozzleLiquidOn { get; set; }
      // Note: NozzleNumber might be implicitly handled or removed
  }

  public class SprayPass {
      public string PassName { get; set; }
      public List<Trajectory> Trajectories { get; set; }
  }

  // Represents canvas zoom/pan state
  public class CanvasViewSettings {
      public double ScaleX { get; set; }
      public double ScaleY { get; set; }
      public double TranslateX { get; set; }
      public double TranslateY { get; set; }
  }

  public class Configuration {
      public string ProductName { get; set; }            // 产品名称
      public List<SprayPass> SprayPasses { get; set; }    // List of spray passes
      public int CurrentPassIndex { get; set; }          // Index of the currently active pass

      public string DxfFileContent { get; set; }         // Embedded DXF file content as string
      public string ModbusIpAddress { get; set; }        // Modbus server IP
      public int ModbusPort { get; set; }                // Modbus server port
      public CanvasViewSettings CanvasState { get; set; } // Saved canvas zoom/pan state
      public int SelectedTrajectoryIndexInCurrentPass { get; set; } // Index of selected trajectory in UI

      // Transform might be handled per trajectory or globally if still needed.
      // public Transform Transform { get; set; }        // (Consider if still global or per-trajectory)
  }
  ```

### 4.6 Modbus通信

- **EasyModbusTCP.NET**（[EasyModbusTCP.NET GitHub](https://github.com/rossmann-engineering/EasyModbusTCP.NET)）：支持Modbus TCP、UDP和RTU，API简单，适合工业自动化。

- **核心通信流程**：
    1.  **连接**：用户通过界面输入IP地址和端口号连接到Modbus服务器。
    2.  **状态检查（发送前）**：在发送配置或执行测试运行前，软件会读取机械臂状态寄存器（默认为地址1000）。
        - `1`：表示机械臂就绪。
        - `0`：表示机械臂忙碌。
        - `2`：表示机械臂故障。
        软件仅在状态为 `1` 时继续。
    3.  **发送配置**：将当前选定的活动喷涂遍（Active Spray Pass）中的轨迹数据发送到机械臂。
        -   发送轨迹数量（写入地址 `1000`，注意：此地址在发送配置时被覆盖，不同于状态读取时的用途）。
        -   对于每条轨迹（最多5条，由 `MaxTrajectories` 定义）：
            -   点数（写入 `1001 + i * TrajectoryRegisterOffset`）。
            -   X坐标数组（写入 `1002 + i * TrajectoryRegisterOffset`）。
            -   Y坐标数组（写入 `1052 + i * TrajectoryRegisterOffset`）。
            -   （旧）喷嘴编号（写入 `1102 + i * TrajectoryRegisterOffset`，根据新喷嘴逻辑，此项可能已调整或其值有特定含义）。
            -   喷淋类型（写入 `1103 + i * TrajectoryRegisterOffset`）：`1` 表示任一液阀开启，`0` 表示仅气阀或无喷淋。
            -   *注意：Z轴坐标和旋转角度（Rx, Ry, Rz）目前主要用于生成`RobTeach_SendData_*.txt`文件，Modbus直接发送的数据主要基于X,Y坐标和简化的喷淋类型。详细的3D姿态控制依赖于机器人控制器对该文本文件的解析和执行。*
    4.  **测试运行**：
        -   用户选择速度模式：慢速（值 `11`）或标准速度（值 `22`）。
        -   将所选速度模式值写入速度控制寄存器（默认为地址 `1001`）。
        -   短暂延时后，向测试运行触发寄存器（默认为地址 `1002`）写入特定值（如 `33`）以启动。
    5.  **断开连接**：用户可以手动断开Modbus连接。

- **主要Modbus寄存器地址（示例，具体以实际控制器为准）**：
    - `1000`：状态读取（忙碌/就绪/错误） 或 写入轨迹总数（发送配置时）。
    - `1001`：轨迹1点数（发送配置时） 或 写入速度模式（测试运行时）。
    - `1002`：轨迹1 X坐标基地址（发送配置时） 或 写入测试运行触发（测试运行时）。
    - `1052`：轨迹1 Y坐标基地址。
    - `1102`：轨迹1喷嘴编号（可能已调整）。
    - `1103`：轨迹1喷淋类型。
    - `TrajectoryRegisterOffset`（代码内常量，通常为100）：用于计算后续轨迹的寄存器基地址。

## 4.7 输出数据文件 (Output Data File)

除了通过Modbus直接与机器人通信外，软件还会生成一个详细的文本数据文件，通常位于程序运行目录下的 `log` 子目录中，文件名格式为 `RobTeach_SendData_YYYYMMDD_HHMMSS_fff.txt`。该文件旨在为机器人提供一个可供直接解析或进一步处理的完整加工程序。

文件内容结构如下：

1.  **总喷涂遍数 (Total Number of Passes)**：一个浮点数，表示配置中包含的总喷涂遍数量。
2.  **对于每一个喷涂遍 (For each Spray Pass)**：
    a.  **遍内基元总数 (Number of Primitives in Pass)**：一个浮点数，表示当前喷涂遍包含的轨迹（基元）数量。
    b.  **对于每一个基元/轨迹 (For each Primitive/Trajectory in Pass)**：
        i.  **基元序号 (Primitive Index)**：一个浮点数，表示当前基元在本遍内的序号（从1开始）。
        ii. **基元类型 (Primitive Type)**：一个浮点数，`1.0` 代表直线 (Line)，`2.0` 代表圆 (Circle)，`3.0` 代表圆弧 (Arc)。
        iii. **上喷嘴气阀状态 (Upper Nozzle Gas Status)**：`11.0` (开) 或 `10.0` (关)。
        iv. **上喷嘴液阀状态 (Upper Nozzle Liquid Status)**：`12.0` (开) 或 `10.0` (关)。
        v.  **下喷嘴气阀状态 (Lower Nozzle Gas Status)**：`21.0` (开) 或 `20.0` (关)。
        vi. **下喷嘴液阀状态 (Lower Nozzle Liquid Status)**：`22.0` (开) 或 `20.0` (关)。
        vii. **末端执行器速度 (End Effector Speed)**：一个浮点数，单位为米/秒 (m/s)，根据轨迹长度和用户设定的加工时间（Runtime）计算得出。
        viii. **基元几何数据 (Primitive Geometry Data)**：
            -   **直线 (Line)**：包含起点和终点两个点的完整坐标。每个点6个值：X, Y, Z, Rx, Ry, Rz (均为浮点数，格式化为三位小数)。
            -   **圆弧 (Arc)**：包含圆弧起点 (P1)、圆弧上一点 (P2 - 通常是中点)、圆弧终点 (P3) 三个点的完整坐标。每个点6个值：X, Y, Z, Rx, Ry, Rz。
            -   **圆 (Circle)**：包含圆上三个点 (P1, P2, P3) 的完整坐标，用于定义圆。每个点6个值：X, Y, Z, Rx, Ry, Rz。
            -   *Rx, Ry, Rz 代表工具坐标系相对于基坐标系的姿态角，通常由用户在轨迹参数中设定或通过CAD图纸的3D信息间接获得。*
        ix. **预留值 (Reserved Values)**：3个浮点数值，默认为 `0.0`，供未来扩展。

该文件的生成确保了即使在复杂的轨迹包含3D姿态信息时，机器人也能获得完整的加工指令。

## 5. 实现步骤

### 5.1 项目初始化

- 创建C# WPF项目，安装NuGet包：IxMilia.Dxf、EasyModbusTCP、Newtonsoft.Json。
- 配置项目支持.NET Framework或.NET Core，确保兼容性。

### 5.2 CAD解析模块

- 使用IxMilia.Dxf加载DXF文件：

  ```csharp
  // using IxMilia.Dxf;
  DxfFile dxfFile = DxfFile.Load("sample.dxf");
  // Access entities via dxfFile.Entities collection
  // Example:
  // var lines = dxfFile.Entities.OfType<DxfLine>();
  // var arcs = dxfFile.Entities.OfType<DxfArc>();
  // var circles = dxfFile.Entities.OfType<DxfCircle>();
  // var lwPolylines = dxfFile.Entities.OfType<DxfLwPolyline>();
  ```

- 将实体转换为WPF可绘制的形状，显示在Canvas上。

### 5.3 轨迹生成模块

- **从CAD实体创建Trajectory对象**：
  - 当用户从CAD视图中选择一个实体（如 `DxfLine`, `DxfArc`, `DxfCircle`）时，会创建一个 `Trajectory` 对象。
  - 原始DXF实体信息（或其句柄/ID）被保存，用于后续可能的重选或高亮。
  - 根据实体类型，填充 `Trajectory`对象的 `PrimitiveType` 及相应的几何参数：
    - **Line**: `LineStartPoint` 和 `LineEndPoint` 直接从 `DxfLine` 的P1, P2获取。
    - **Arc**: 从 `DxfArc` 的圆心、半径、起止角计算出定义圆弧的三个关键点（P1, P2, P3），并存入 `ArcPoint1`, `ArcPoint2`, `ArcPoint3`。这些点也包含Z坐标和默认姿态角。
    - **Circle**: 从 `DxfCircle` 的圆心、半径、法向量计算出定义圆的三个关键点（P1, P2, P3），存入 `CirclePoint1`, `CirclePoint2`, `CirclePoint3`。
- **轨迹点集生成（`PopulateTrajectoryPoints`）**：
  - `Trajectory` 对象有一个非持久化的 `Points` 列表 ( `List<System.Windows.Point>` )，用于WPF渲染和预览。
  - 此列表根据 `PrimitiveType` 和相应的几何参数（如 `LineStartPoint`/`LineEndPoint` 或 `ArcPoint1/2/3` 等）动态生成。
  - 对于圆弧和圆，会使用 `GeometryUtils.CalculateArcParametersFromThreePoints` 或 `GeometryUtils.CalculateCircleCenterRadiusFromThreePoints` 等辅助方法，通过三点定义反算出圆心、半径、起止角等参数，然后根据预设的分辨率（`TrajectoryPointResolutionAngle`）进行离散化，生成 `Points` 列表。
  - 轨迹的 `IsReversed` 属性会影响点生成的顺序。
- **属性赋值**：
  - 新创建的 `Trajectory` 对象会被赋予默认的喷嘴设置、Runtime（基于最小长度/速度计算）等。这些属性后续可在UI中编辑。

- 示例代码（概念性，实际实现分布在 `MainWindow.xaml.cs` 的事件处理、 `CadService.cs` 和 `GeometryUtils.cs` 中）：

  ```csharp
  // In MainWindow when a DxfArc is selected:
  // var newTrajectory = new Trajectory { PrimitiveType = "Arc", OriginalDxfEntity = dxfArcEntity };
  // // ... code to calculate P1, P2, P3 from dxfArcEntity properties ...
  // newTrajectory.ArcPoint1 = calculatedP1; // Type TrajectoryPointWithAngles
  // newTrajectory.ArcPoint2 = calculatedP2;
  // newTrajectory.ArcPoint3 = calculatedP3;
  // PopulateTrajectoryPoints(newTrajectory); // Generates List<Point> for display
  // currentPass.Trajectories.Add(newTrajectory);

  // In PopulateTrajectoryPoints(Trajectory trajectory) for an Arc:
  // if (trajectory.PrimitiveType == "Arc") {
  //     var arcParams = GeometryUtils.CalculateArcParametersFromThreePoints(
  //         trajectory.ArcPoint1.Coordinates,
  //         trajectory.ArcPoint2.Coordinates,
  //         trajectory.ArcPoint3.Coordinates);
  //     if (arcParams.HasValue) {
  //         var (center, radius, startAngle, endAngle, normal, isClockwise) = arcParams.Value;
  //         // ... loop from startAngle to endAngle with resolution to generate points ...
  //         // trajectory.Points.Add(new Point(x,y));
  //     }
  // }
  ```

### 5.4 用户界面模块

- 设计主窗口，包含：
  - CAD显示区域：使用Canvas绘制实体。
  - 轨迹选择面板：列表或选项卡显示最多五条轨迹。
  - 参数设置区域：喷嘴编号和喷淋类型输入框。
  - 按钮：加载CAD、保存/加载配置、发送数据。
- 支持交互：
  - 鼠标点击选择实体，高亮显示。
  - 缩放和平移功能，增强用户体验。
  - 轨迹预览，显示生成的点序列。

### 5.5 配置管理模块

- 实现保存和加载功能：

  ```csharp
  public void SaveConfig(Configuration config, string filePath) {
      string json = JsonConvert.SerializeObject(config, Formatting.Indented);
      File.WriteAllText(filePath, json);
  }
  public Configuration LoadConfig(string filePath) {
      string json = File.ReadAllText(filePath);
      return JsonConvert.DeserializeObject<Configuration>(json);
  }
  ```

### 5.6 Modbus通信模块

- 配置Modbus客户端：

  ```csharp
  ModbusClient modbusClient = new ModbusClient("192.168.0.1", 502);
  modbusClient.Connect();
  ```

- 发送轨迹数据：

  ```csharp
  modbusClient.WriteSingleRegister(1000, config.Trajectories.Count);
  for (int i = 0; i < config.Trajectories.Count; i++) {
      var traj = config.Trajectories[i];
      modbusClient.WriteSingleRegister(1001 + i * 100, traj.Points.Count);
      // 写入X、Y坐标、喷嘴编号和喷淋类型
  }
  modbusClient.Disconnect();
  ```

### 5.7 测试与优化

- **单元测试**：测试CAD解析、轨迹生成和配置管理模块。
- **集成测试**：使用Modbus模拟器（如Modbus Slave）验证通信。
- **性能优化**：优化CAD渲染和轨迹生成算法，处理大型DXF文件。
- **用户反馈**：根据实际使用调整界面和功能。

## 6. 注意事项

- **Modbus寄存器映射**：需获取机械臂控制器的Modbus文档，确认数据格式和寄存器分配。
- **坐标系匹配**：确保CAD坐标与机械臂工作空间一致，可能需用户设置变换参数。
- **错误处理**：
  - 无效CAD文件：提示用户重新选择。
  - 通信失败：重试机制或错误提示。
  - 轨迹无效：验证点序列连续性。
- **扩展性**：支持3D CAD（如STEP格式）或更多轨迹，需使用Open CASCADE Technology（[Open CASCADE](https://www.opencascade.com/)）。
- **安全性**：确保Modbus通信加密（如使用VPN），防止数据泄露。

## 7. 开发计划

| 阶段          | 任务内容                             | 预计时间 |
| ------------- | ------------------------------------ | -------- |
| 项目初始化    | 搭建C# WPF项目，集成库               | 1周      |
| CAD解析与显示 | 实现DXF解析和WPF渲染                 | 2周      |
| 轨迹生成      | 开发实体到点序列的转换算法           | 2周      |
| 用户界面      | 设计交互界面，支持轨迹选择和参数设置 | 3周      |
| 配置管理      | 实现JSON保存和加载                   | 1周      |
| Modbus通信    | 实现数据发送，测试通信               | 2周      |
| 测试与优化    | 单元测试、集成测试、性能优化         | 3周      |

## 8. 结论

本设计方案通过模块化架构和成熟技术栈，满足用户对上位机换型软件的需求。使用C#和WPF开发，结合netDxf和EasyModbusTCP.NET，确保功能实现和用户体验。后续开发需重点确认机械臂的Modbus接口，并根据用户反馈优化功能。
