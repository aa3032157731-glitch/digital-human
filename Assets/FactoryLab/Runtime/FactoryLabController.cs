using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FactoryLab
{
    public sealed partial class FactoryLabController : MonoBehaviour
    {
        public TextAsset caseData, layoutData;
        public Material[] palette;
        public Camera sceneCamera;
        public FactoryStationView[] views;
        public FactorySimulation Simulation { get; private set; }
        public bool firstPersonCourse = true;
        public Canvas Hud { get; private set; }
        public bool Running { get; private set; }
        public int Speed { get; private set; } = 1;
        public int Quantity { get; private set; } = 20;
        public string LastExportPath { get; private set; }
        private Transform dynamicRoot, carrier;
        private Transform[] products;
        private Text headingStatus, stats, detail, message, eventText;
        private Text[] stationTexts, productTexts;
        private Button[] quantityButtons, speedButtons;
        private Button startButton, pauseButton;
        private Font font;
        private int selectedStation = 1, selectedProduct = 0;
        private float yaw = -24, pitch = 48, distance = 17;
        private bool topDown, closeUp;
        private Vector3 target = new Vector3(6, 0, 4);
        private LineRenderer routeLine;
        private static readonly Color Ink = new Color(.075f, .13f, .19f), Muted = new Color(.38f, .45f, .50f);
        private static readonly Color Accent = new Color(.02f, .43f, .42f);

        private void Start() { Initialize(); }
        public void Initialize()
        {
            if (Hud != null) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Microsoft YaHei UI", "Arial" }, 22);
            if(Application.isPlaying && !Application.isEditor && !Environment.GetCommandLineArgs().Contains("-factory-selftest"))
                Screen.SetResolution(1366,768,FullScreenMode.Windowed);
            BuildHud();
            InitializeCourse();
            ResetExperiment(20);
            if (firstPersonCourse) InitializeWalkCourse();
            UpdateCamera();
        }

        public void ResetExperiment(int quantity)
        {
            if (quantity != 1 && quantity != 20) throw new ArgumentOutOfRangeException(nameof(quantity));
            Running = false; Quantity = quantity; selectedProduct = 0;
            InvalidateTeacher();
            ExperimentId = Guid.NewGuid().ToString("N");
            Simulation = new FactorySimulation(CurrentConfiguration(), quantity);
            if (dynamicRoot != null)
            {
                dynamicRoot.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(dynamicRoot.gameObject); else DestroyImmediate(dynamicRoot.gameObject);
            }
            dynamicRoot = FactoryGeometry.Empty(transform, "Runtime visuals", Vector3.zero);
            carrier = FactoryGeometry.CreateCarrier(dynamicRoot, palette);
            products = new Transform[quantity];
            for (int i = 0; i < quantity; i++) products[i] = FactoryGeometry.CreateProduct(dynamicRoot, Simulation.Products[i].Id, palette);
            var line = FactoryGeometry.Empty(dynamicRoot, "Process route", Vector3.zero).gameObject;
            routeLine = line.AddComponent<LineRenderer>(); routeLine.sharedMaterial = palette[7];
            routeLine.startWidth = routeLine.endWidth = .045f; routeLine.useWorldSpace = true;
            var points = Enumerable.Range(0, 5).SelectMany(i => Simulation.Config.Path(Simulation.Config.Ports[i], Simulation.Config.Ports[i + 1]))
                .Select(p => new Vector3((float)p.x, .1f, (float)p.z)).ToArray();
            routeLine.positionCount = points.Length; routeLine.SetPositions(points);
            SetMessage("已重置。先观察工位和搬运路线，再开始订单。");
            RefreshView();
        }

        public void StartExperiment()
        {
            if (firstPersonCourse) { StartWalkExperiment(); return; }
            if(!IsBaseline && Simulation.Time==0 && Workbook.prediction.Trim().Length<12){OpenLesson(4);modalStatus.text="请先填写至少 12 字的运行前预测，再返回开始改善订单。";return;}
            if (!Simulation.Complete) { Running = true; SetMessage("订单运行中 · 参数为教学设定"); }
        }
        public void PauseExperiment() { Running = false; SetMessage("已暂停，可查看工位和产品事件。"); }
        public void SetSpeed(int speed) { if (speed != 1 && speed != 5 && speed != 10) throw new ArgumentOutOfRangeException(nameof(speed)); Speed = speed; }
        public void SelectStation(int index) { selectedStation = index; if (closeUp) UpdateCamera(); RefreshView(); }
        public void SelectProduct(int index) { if (index >= 0 && index < Quantity) selectedProduct = index; RefreshView(); }
        public void SetView(int mode) { topDown = mode == 1; closeUp = mode == 2; UpdateCamera(); }

        private void Update()
        {
            if (Simulation == null) return;
            float scale=Hud.scaleFactor;
            if (!firstPersonCourse) sceneCamera.rect=new UnityEngine.Rect(404*scale/Screen.width,(Screen.height-820*scale)/Screen.height,1497*scale/Screen.width,615*scale/Screen.height);
            if (Running)
            {
                try { Simulation.Advance(Time.unscaledDeltaTime * Speed); }
                catch (Exception ex) { Running = false; SetMessage("仿真已停止：" + ex.Message); Debug.LogException(ex); }
                if (Simulation.Complete) { Running = false; SaveCompletedRun(); SetMessage("订单完成，结果已保存。进入布局改善可比较同参数实验。"); }
            }
            UpdateCourse();
            if (firstPersonCourse) { UpdateWalkCourse(); RefreshView(); return; }
            bool pointerOnUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!pointerOnUI && sceneCamera.pixelRect.Contains(Input.mousePosition))
            {
                if (Input.GetMouseButtonDown(0) && Physics.Raycast(sceneCamera.ScreenPointToRay(Input.mousePosition), out var hit, 150))
                {
                    var station = hit.collider.GetComponentInParent<FactoryStationView>();
                    if (station != null) SelectStation(station.stationIndex);
                }
                if (!topDown && Input.GetMouseButton(1)) { yaw += Input.GetAxis("Mouse X") * 2; pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 2, 25, 80); UpdateCamera(); }
                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 0) { distance = Mathf.Clamp(distance - scroll, 10, 24); UpdateCamera(); }
            }
            RefreshView();
        }

        private void UpdateCamera()
        {
            if (firstPersonCourse) return;
            if (sceneCamera == null) return;
            target = closeUp && views != null ? views[selectedStation].transform.position + Vector3.up * .35f : new Vector3(6, 0, 4);
            sceneCamera.orthographic = topDown;
            if (topDown)
            {
                sceneCamera.orthographicSize = distance * .28f;
                sceneCamera.transform.position = target + Vector3.up * 20;
                sceneCamera.transform.rotation = Quaternion.Euler(90, 0, 0);
            }
            else
            {
                sceneCamera.transform.position = target + Quaternion.Euler(pitch, yaw, 0) * new Vector3(0, 0, -(closeUp ? 5.5f : distance));
                sceneCamera.transform.LookAt(target);
            }
        }

        public void RefreshView()
        {
            if (Simulation == null || products == null) return;
            var sim = Simulation;
            var cp = sim.CurrentCarrierPoint(); carrier.position = new Vector3((float)cp.x, .07f, (float)cp.z);
            for (int i = 0; i < products.Length; i++)
            {
                var p = sim.Products[i]; Vector3 position = StockPosition(p.Index, p.FinishedAt.HasValue ? 5 : 0);
                for (int s = 1; s <= 4; s++)
                {
                    var state = sim.Stations[s];
                    if (state.Processing == p) position = views[s].workSlot.position;
                    if (state.Output == p) position = views[s].outputSlot.position;
                    int q = 0; foreach (var item in state.Input) { if (item == p) position = views[s].inputSlots[q].position; q++; }
                }
                if (sim.Cargo == p && (sim.Phase == CarrierPhase.LoadedTravel || sim.Phase == CarrierPhase.Unloading)) position = carrier.position + Vector3.up * .31f;
                products[i].position = position;
                // Change the visible product only when the corresponding operation has finished.
                int form = p.ProcessedThrough >= 4 ? 2 : p.ProcessedThrough >= 1 ? 1 : 0;
                for (int c = 0; c < 3; c++) products[i].GetChild(c).gameObject.SetActive(c == form);
                products[i].localScale = Vector3.one * (i == selectedProduct ? 1.17f : 1);
            }
            for (int s = 0; s < 6; s++)
            {
                views[s].selection.SetActive(s == selectedStation);
                var state = sim.Stations[s];
                views[s].statusLamp.sharedMaterial = state.Processing != null ? palette[4] : state.Output != null ? palette[6] : palette[3];
                string stateText = s == 0 ? "库存 " + sim.Products.Count(p => !p.ReleasedAt.HasValue) : s == 5 ? "入库 " + sim.FinishedCount :
                    "待料 " + state.Input.Count + "/" + sim.Config.Stations[s].Capacity + "  " + (state.Processing != null ? "作业中" : state.Output != null ? "待搬运" : "空闲");
                stationTexts[s].text = (s + 1).ToString("00") + "  " + sim.Config.Stations[s].Name + "\n" + stateText;
            }
            headingStatus.text = (sim.Complete ? "订单完成" : Running ? "正在运行" : sim.Time > 0 ? "已暂停" : "准备就绪") + "   ·   " + Speed + "×  播放";
            stats.text = "仿真时间  " + sim.Time.ToString("0.0") + " s     已完成  " + sim.FinishedCount + " / " + Quantity +
                "     载货  " + sim.LoadedDistance.ToString("0.0") + " m     空驶  " + sim.EmptyDistance.ToString("0.0") + " m";
            var selected = sim.Products[selectedProduct];
            double waiting = selected.WaitingSeconds + (selected.Activity == "delay" ? sim.Time - selected.ActivityStart : 0);
            detail.text = "当前工位：" + sim.Config.Stations[selectedStation].Name + "\n设定作业：" + sim.Config.Stations[selectedStation].Cycle + " 秒\n\n" +
                selected.Id + "  ·  " + ActivityName(selected.Activity) + "\n" + ReasonName(selected.Reason) + "\n累计等待 " + waiting.ToString("0.0") + " s · 搬运 " + selected.LoadedDistance.ToString("0.0") + " m";
            var recent = sim.Events.Where(e => e.subject == selected.Id).Reverse().Take(3).Reverse();
            eventText.text = string.Join("\n", recent.Select(e => e.startSeconds.ToString("0.0") + " → " + (e.endSeconds.HasValue ? e.endSeconds.Value.ToString("0.0") : "入库") +
                " s   " + ActivityName(e.activity) + "  ·  " + ReasonName(e.reason)));
            if (eventText.text.Length == 0) eventText.text = "选择产品后，在这里查看最近的活动记录。";
            for (int i = 0; i < productTexts.Length; i++)
            {
                productTexts[i].transform.parent.gameObject.SetActive(i < Quantity);
                if (i >= Quantity) continue;
                productTexts[i].text = (i + 1).ToString("00") + "\n" + (sim.Products[i].FinishedAt.HasValue ? "完成" : ActivityName(sim.Products[i].Activity));
                productTexts[i].transform.parent.GetComponent<Image>().color = i == selectedProduct ? new Color(.72f, .9f, .87f) : new Color(.92f, .94f, .95f);
            }
            startButton.interactable = !Running && !sim.Complete; pauseButton.interactable = Running;
            foreach (var button in quantityButtons) button.interactable = sim.Time == 0 && !Running;
            for (int i = 0; i < speedButtons.Length; i++) speedButtons[i].GetComponent<Image>().color = new[] { 1, 5, 10 }[i] == Speed ? new Color(.72f, .9f, .87f) : Color.white;
        }

        private Vector3 StockPosition(int index, int station)
        {
            return views[station].transform.position + new Vector3(-.72f + index % 5 * .36f, .25f + index / 10 * .48f, -.18f + (index / 5 % 2) * .33f);
        }

        public void ExportEvents()
        {
            try
            {
                string root = Path.Combine(Application.persistentDataPath, "FactoryLab", "Runs");
                Directory.CreateDirectory(root);
                string id = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                LastExportPath = Path.Combine(root, id);
                var sim = Simulation;
                File.WriteAllText(LastExportPath + ".json", JsonConvert.SerializeObject(new {
                    schemaVersion = 1, caseId = "lamp-assembly-process-analysis-v1", layoutId = sim.Config.LayoutId,
                    completed = sim.Complete, elapsedSeconds = sim.Time, quantity = Quantity, finished = sim.FinishedCount,
                    loadedDistanceM = sim.LoadedDistance, emptyDistanceM = sim.EmptyDistance,
                    configuration = sim.Config, events = sim.Events,
                    activeActivities = sim.Products.Where(p => !p.FinishedAt.HasValue).Select(p => new { productId = p.Id, p.Activity, p.Reason, p.ActivityStart })
                }, Formatting.Indented), new UTF8Encoding(false));
                var csv = new StringBuilder("subject,activity,reason,from,to,resource,startSeconds,endSeconds,distanceM\r\n");
                foreach (var e in sim.Events)
                    csv.AppendLine(string.Join(",", new[] { e.subject, e.activity, e.reason, e.from, e.to, e.resource,
                        e.startSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        e.endSeconds?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "",
                        e.distanceM.ToString("R", System.Globalization.CultureInfo.InvariantCulture) }.Select(Csv)));
                File.WriteAllText(LastExportPath + ".csv", csv.ToString(), new UTF8Encoding(true));
                SetMessage("已导出 JSON / CSV。点击“打开记录目录”查看。");
            }
            catch (Exception ex) { SetMessage("导出失败：" + ex.Message); Debug.LogException(ex); }
        }
        private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        private void OpenExports()
        {
            var path = Path.Combine(Application.persistentDataPath, "FactoryLab", "Runs");
            Directory.CreateDirectory(path); Application.OpenURL(new Uri(path).AbsoluteUri);
        }
        private void SetMessage(string text) { if (message != null) message.text = text; }
        public static string ActivityName(string key)
        {
            switch (key) { case "operation": return "加工"; case "inspection": return "检查"; case "transport": return "搬运"; case "delay": return "等待"; case "storage": return "储存"; default: return key; }
        }
        public static string ReasonName(string key)
        {
            switch (key) { case "controlled-stock": return "供料库存"; case "finished-stock": return "成品入库"; case "processing": return "工位作业";
                case "waiting-carrier": return "等待搬运车"; case "downstream-full": return "下游缓冲已满"; case "output-blocked": return "输出料位占用";
                case "waiting-station": return "等待工位"; case "loaded-transfer": return "装载、移动与卸载"; default: return key; }
        }

        private RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); return rect;
        }
        private RectTransform Panel(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var rect = Rect(parent, name, x, y, w, h); rect.gameObject.AddComponent<Image>().color = color; return rect;
        }
        private Text Text(Transform parent, string text, float x, float y, float w, float h, int size = 20, Color? color = null)
        {
            var label = Rect(parent, "Text", x, y, w, h).gameObject.AddComponent<Text>(); label.font = font;
            label.text = text; label.fontSize = size; label.color = color ?? Ink; label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate; return label;
        }
        private Button Button(Transform parent, string label, float x, float y, float w, float h, Action action)
        {
            var rect = Panel(parent, label, x, y, w, h, Color.white); var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>(); button.onClick.AddListener(() => action());
            var colors = button.colors; colors.highlightedColor = new Color(.82f, .92f, .92f); colors.pressedColor = new Color(.7f, .86f, .85f); button.colors = colors;
            var text = Text(rect, label, 6, 3, w - 12, h - 6); text.alignment = TextAnchor.MiddleCenter; return button;
        }
        private void BuildHud()
        {
            var canvas = new GameObject("Factory HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform, false); Hud = canvas.GetComponent<Canvas>(); Hud.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            if (FindObjectOfType<EventSystem>() == null)
            {
                var events = new GameObject("Factory EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)); events.transform.SetParent(transform);
            }
            var top = Panel(canvas.transform, "Header", 28, 20, 1864, 76, Ink);
            Text(top, "FACTORY LAB  /  程序分析实训", 24, 12, 1040, 38, 30, Color.white);
            Text(top, "台灯装配车间 · 第四章 · 观察产品流动与等待", 26, 50, 1050, 25, 17, new Color(.64f, .8f, .8f));
            headingStatus = Text(top, "准备就绪", 1350, 26, 480, 32, 23, Color.white);
            var side = Panel(canvas.transform, "Controls", 28, 112, 346, 942, new Color(.96f, .97f, .98f));
            Text(side, "观察与运行", 20, 18, 300, 32, 25);
            Text(side, "订单数量 · 重置后可更改", 20, 61, 310, 26, 18, Muted);
            quantityButtons = new[] { Button(side, "单件验证", 20, 98, 147, 42, () => ResetExperiment(1)), Button(side, "20 件订单", 179, 98, 147, 42, () => ResetExperiment(20)) };
            startButton = Button(side, "开始 / 继续", 20, 154, 147, 44, StartExperiment);
            pauseButton = Button(side, "暂停", 179, 154, 147, 44, PauseExperiment);
            Button(side, "重置实验", 20, 211, 147, 40, () => ResetExperiment(Quantity));
            Button(side, "路线开关", 179, 211, 147, 40, () => routeLine.enabled = !routeLine.enabled);
            speedButtons = new Button[3];
            for (int i = 0; i < 3; i++) { int speed = new[] { 1, 5, 10 }[i]; speedButtons[i] = Button(side, speed + "×", 20 + 105 * i, 266, 96, 38, () => SetSpeed(speed)); }
            Text(side, "工位 · 点击选中", 20, 327, 300, 26, 19, Muted);
            stationTexts = new Text[6];
            for (int i = 0; i < 6; i++)
            {
                int index = i; var button = Button(side, "", 20, 365 + 53 * i, 306, 46, () => SelectStation(index));
                stationTexts[i] = button.GetComponentInChildren<Text>(); stationTexts[i].fontSize = 16; stationTexts[i].alignment = TextAnchor.MiddleLeft;
            }
            detail = Text(side, "", 20, 695, 310, 172, 18);
            Button(side, "导出事件", 20, 884, 147, 40, ExportEvents);
            Button(side, "打开记录目录", 179, 884, 147, 40, OpenExports);
            stats = Text(canvas.transform, "", 410, 113, 1460, 38, 23);
            Button(canvas.transform, "全景", 1490, 162, 118, 38, () => SetView(0));
            Button(canvas.transform, "俯视", 1620, 162, 118, 38, () => SetView(1));
            Button(canvas.transform, "工位近景", 1750, 162, 140, 38, () => SetView(2));
            Text(canvas.transform, "右键拖动旋转 · 滚轮缩放 · 点击工位查看状态", 410, 165, 990, 32, 18, Muted);
            message = Text(canvas.transform, "", 410, 820, 1460, 36, 20, Accent);
            var bottom = Panel(canvas.transform, "Product strip", 404, 870, 1488, 184, new Color(.96f, .97f, .98f));
            Text(bottom, "跟踪产品", 16, 12, 800, 29, 22);
            productTexts = new Text[20];
            for (int i = 0; i < 20; i++)
            {
                int index = i; var b = Button(bottom, "", 16 + i % 10 * 61, 54 + i / 10 * 55, 54, 49, () => SelectProduct(index));
                productTexts[i] = b.GetComponentInChildren<Text>(); productTexts[i].fontSize = 17;
            }
            Text(bottom, "最近事件 · 当前选中产品", 670, 12, 760, 30, 19, Muted);
            eventText = Text(bottom, "", 670, 56, 795, 119, 19);
        }
    }
}
