using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FactoryLab
{
    public sealed partial class FactoryLabController
    {
        public string ExperimentId { get; private set; }
        public FactoryRun Baseline { get; private set; }
        public FactoryRun Improved { get; private set; }
        public FactoryWorkbook Workbook { get; private set; } = new FactoryWorkbook();
        public string LastReportPath { get; private set; }
        public bool IsBaseline => currentLayout == null;
        private string currentLayout;
        private GameObject courseModal;
        private Text modalStatus, teacherAnswer;
        private LineRenderer historyLine;
        private int pathStage = -1, pathProduct = -1, hintLevel;
        private int swapFirst = 1, swapSecond = 3;
        private string savedRunId;
        public static string StorageOverride;
        private string CourseDirectory => firstPersonCourse ? V2Directory : StorageOverride ?? Path.Combine(Application.persistentDataPath, "FactoryLab", "Course");
        private FactoryConfiguration CurrentConfiguration() => FactoryConfiguration.Read(caseData.text, currentLayout ?? layoutData.text, IsBaseline ? "baseline" : "student-layout");

        private void InitializeCourse()
        {
            if (firstPersonCourse) return;
            Button(Hud.transform, "布局改善", 790, 44, 155, 40, OpenLayout);
            Button(Hud.transform, "实训任务", 959, 44, 155, 40, () => OpenLesson(Workbook.step));
            Button(Hud.transform, "数字人教师", 1128, 44, 178, 40, OpenTeacher);
            if (Application.isPlaying)
            {
                try
                {
                    string path = Path.Combine(CourseDirectory,"workbook.json");
                    if(File.Exists(path)) Workbook = JsonConvert.DeserializeObject<FactoryWorkbook>(File.ReadAllText(path)) ?? new FactoryWorkbook();
                    if(Workbook.choices == null || Workbook.choices.Length != 15 || Workbook.submitted == null || Workbook.submitted.Length != 6) Workbook = new FactoryWorkbook();
                    string runs = Path.Combine(CourseDirectory,"comparison.json");
                    if(File.Exists(runs)) { var data=JObject.Parse(File.ReadAllText(runs)); Baseline=data["baseline"]?.ToObject<FactoryRun>(); Improved=data["improved"]?.ToObject<FactoryRun>(); }
                }
                catch(Exception e) { Debug.LogWarning("实验草稿无法恢复："+e.Message); Workbook = new FactoryWorkbook(); }
            }
        }
        private void UpdateCourse()
        {
            UpdateTeacher();
            if (Simulation == null) return;
            int stage=Simulation.Products[selectedProduct].Station;
            if(pathStage!=stage || pathProduct!=selectedProduct || historyLine==null)
            {
                pathStage=stage; pathProduct=selectedProduct;
                if(historyLine==null) { historyLine=FactoryGeometry.Empty(dynamicRoot,"Selected product history",Vector3.zero).gameObject.AddComponent<LineRenderer>(); historyLine.sharedMaterial=palette[3]; historyLine.startWidth=historyLine.endWidth=.09f; }
                var path=Enumerable.Range(0,stage).SelectMany(i=>Simulation.Config.Path(Simulation.Config.Ports[i],Simulation.Config.Ports[i+1])).Select(p=>new Vector3((float)p.x,.13f,(float)p.z)).ToArray();
                historyLine.positionCount=path.Length; if(path.Length>0) historyLine.SetPositions(path);
            }
        }
        public void SaveCompletedRun()
        {
            if(!Simulation.Complete || savedRunId==ExperimentId) return;
            var run=FactoryRun.Capture(Simulation,ExperimentId);
            if(IsBaseline)
            {
                if(Baseline!=null && Baseline.id!=run.id){Workbook.submitted[3]=Workbook.submitted[4]=Workbook.submitted[5]=false;Workbook.routeEvidence=Workbook.waitEvidence="";}
                Baseline=run;
            }
            else Improved=run;
            savedRunId=ExperimentId;
            if (firstPersonCourse) { CaptureWalkResult(run); return; }
            if(Application.isPlaying)
            {
                try { Directory.CreateDirectory(CourseDirectory); File.WriteAllText(Path.Combine(CourseDirectory,"comparison.json"),JsonConvert.SerializeObject(new { baseline=Baseline, improved=Improved },Formatting.Indented)); }
                catch(Exception e) { SetMessage("结果已保留在内存，但保存失败："+e.Message); }
            }
        }
        public void SwapStations(int first, int second)
        {
            if(Running || Simulation.Time>0) throw new InvalidOperationException("先重置实验，再交换工位。完成结果会保留。");
            if(first<1 || first>4 || second<1 || second>4 || first==second) throw new ArgumentException("只允许选择两个不同的作业工位；供料和成品区固定。");
            var json=JObject.Parse(currentLayout ?? layoutData.text);
            var layout=json["layouts"].First(x=>(string)x["id"]==(IsBaseline ? "baseline":"student-layout"));
            var positions=layout["positions"]; string a=(string)positions[FactoryConfiguration.Route[first]];
            positions[FactoryConfiguration.Route[first]]=positions[FactoryConfiguration.Route[second]].DeepClone(); positions[FactoryConfiguration.Route[second]]=a;
            layout["id"]="student-layout"; currentLayout=json.ToString();
            RebuildLayout();
        }
        public void RestoreBaseline() { currentLayout=null; RebuildLayout(); }
        private void RebuildLayout()
        {
            Running=false;
            var old=transform.Find("Generated factory");
            if(old!=null) { old.gameObject.SetActive(false); old.SetParent(null); if(Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject); }
            views=FactoryGeometry.Build(FactoryGeometry.Empty(transform,"Generated factory",Vector3.zero),CurrentConfiguration(),palette);
            ResetExperiment(Quantity); SetView(1);
            if (firstPersonCourse && walker != null) PrepareWalkGeometry();
        }
        private RectTransform Modal(string title)
        {
            if (firstPersonCourse && walker != null) walker.SetWalking(false);
            PauseExperiment(); CloseModal();
            courseModal=Panel(Hud.transform,"Course modal",0,0,1920,1080,new Color(.04f,.09f,.14f,.80f)).gameObject;
            var panel=Panel(courseModal.transform,title,140,112,1640,900,new Color(.96f,.98f,.98f));
            Text(panel,title,32,22,1170,48,30);
            Button(panel,"记录目录",1235,20,176,44,()=>{Directory.CreateDirectory(CourseDirectory);Application.OpenURL(new Uri(CourseDirectory).AbsoluteUri);});
            Button(panel,"返回工厂",1430,20,176,44,CloseModal);
            modalStatus=Text(panel,"实验已暂停，返回后点击开始 / 继续。",32,805,1560,66,20,Accent);
            return panel;
        }
        public void CloseModal()
        {
            if(courseModal!=null) { courseModal.SetActive(false); if(Application.isPlaying) Destroy(courseModal); else DestroyImmediate(courseModal); courseModal=null; }
            ShowTeacher(false);
            if (firstPersonCourse && walker != null) { walker.SetWalking(false); ShowResume(); }
        }
        public void OpenLayout()
        {
            if (firstPersonCourse) { OpenWalkTask(); return; }
            var panel=Modal("布局改善 / 同参数比较");
            Text(panel,"① 完成原始订单 → ② 重置并交换工位 → ③ 提交预测 → ④ 重跑同样订单",32,83,1540,36,23);
            Text(panel,"当前："+(IsBaseline?"原始布局":"学生改善布局")+"   ·   单件载货路线 "+CurrentConfiguration().ProductDistance().ToString("0.0")+" m",32,132,1540,34,23);
            Text(panel,"选择两个作业工位",32,194,670,32,22);
            Choice(panel,Simulation.Config.Stations.Skip(1).Take(4).Select(s=>s.Name).ToArray(),swapFirst-1,32,241,290,46,v=>swapFirst=v+1);
            Choice(panel,Simulation.Config.Stations.Skip(1).Take(4).Select(s=>s.Name).ToArray(),swapSecond-1,348,241,290,46,v=>swapSecond=v+1);
            Button(panel,"交换工位",32,318,290,46,()=>{try{SwapStations(swapFirst,swapSecond);OpenLayout();}catch(Exception e){modalStatus.text=e.Message;}});
            Button(panel,"重置当前实验",348,318,290,46,()=>{ResetExperiment(Quantity);OpenLayout();});
            Button(panel,"恢复原始布局",32,383,606,46,()=>{RestoreBaseline();OpenLayout();});
            Text(panel,"供料区与成品区固定；不改变工序、人数、质量要求或工时。\n\n青绿色粗线：选中产品已完成的搬运路径。\n金色细线：当前布局的完整工艺路线。\n\n运行结果自动保存。交换前须重置；重置不会删除已保存结果。",32,462,630,240,22);
            var card=Panel(panel,"Comparison",704,191,900,527,Color.white);
            Text(card,FactoryRun.Compare(Baseline,Improved),24,20,852,470,23);
            Button(panel,"填写运行前预测",32,736,290,46,()=>OpenLesson(4));
            Button(panel,"导出当前实验记录",704,736,360,46,ExportReport);
        }
        private InputField Entry(Transform parent,string initial,float x,float y,float w,float h,Action<string> changed)
        {
            var rect=Panel(parent,"Text entry",x,y,w,h,Color.white); var input=rect.gameObject.AddComponent<InputField>();
            var label=Text(rect,initial,12,9,w-24,h-18,21); input.textComponent=label; input.text=initial; input.lineType=InputField.LineType.MultiLineNewline;
            input.characterLimit=3000; input.onValueChanged.AddListener(v=>{changed(v);SaveWorkbook();}); return input;
        }
        private Dropdown Choice(Transform parent,string[] options,int value,float x,float y,float w,float h,Action<int> changed)
        {
            var root=Panel(parent,"Choice",x,y,w,h,Color.white); var dd=root.gameObject.AddComponent<Dropdown>(); dd.targetGraphic=root.GetComponent<Image>();
            dd.captionText=Text(root,"",12,5,w-42,h-10,20); Text(root,"▾",w-32,5,26,h-10,20);
            var template=Panel(root,"Template",0,h,w,Mathf.Min(250,options.Length*42+8),Color.white);
            var scroll=template.gameObject.AddComponent<ScrollRect>(); scroll.horizontal=false;
            var viewport=Panel(template,"Viewport",0,0,w,template.sizeDelta.y,Color.white); viewport.gameObject.AddComponent<RectMask2D>();
            var content=Rect(viewport,"Content",0,0,w,42); var item=Panel(content,"Item",0,0,w,42,Color.white); var toggle=item.gameObject.AddComponent<Toggle>(); toggle.targetGraphic=item.GetComponent<Image>();
            var check=Panel(item,"Check",4,15,10,10,Accent); toggle.graphic=check.GetComponent<Image>();
            dd.itemText=Text(item,"",24,4,w-30,34,20); dd.template=template; dd.options=options.Select(o=>new Dropdown.OptionData(o)).ToList();
            scroll.viewport=viewport; scroll.content=content; template.gameObject.SetActive(false); dd.value=Mathf.Clamp(value,0,options.Length-1); dd.RefreshShownValue();
            dd.onValueChanged.AddListener(v=>changed(v)); return dd;
        }
        private void SaveWorkbook()
        {
            if(firstPersonCourse){SaveWalkProgress();return;}
            if(!Application.isPlaying)return;
            try {Directory.CreateDirectory(CourseDirectory);File.WriteAllText(Path.Combine(CourseDirectory,"workbook.json"),JsonConvert.SerializeObject(Workbook,Formatting.Indented));}
            catch(Exception e){if(modalStatus!=null)modalStatus.text="草稿保存失败："+e.Message;}
        }
        public void OpenLesson(int step)
        {
            if (firstPersonCourse) { OpenWalkTask(); return; }
            Workbook.step=Mathf.Clamp(step,0,5); hintLevel=0; var panel=Modal("25 分钟实训 / "+FactoryLesson.Steps[Workbook.step]);
            for(int i=0;i<6;i++){int index=i;var b=Button(panel,(Workbook.submitted[i]?"✓ ":"")+(i+1)+" "+FactoryLesson.Steps[i].Split('·')[0],32+i*263,83,245,44,()=>OpenLesson(index));if(i==Workbook.step)b.GetComponent<Image>().color=new Color(.72f,.9f,.87f);}
            int s=Workbook.step;
            if(s==0)
            {
                Text(panel,"先选择研究边界，再用自己的话写出改善目标。",32,158,1500,40,25);
                Choice(panel,new[]{"请选择研究对象","单件台灯及配套物料","搬运车的全部任务","所有产品混在一起"},Workbook.researchObject+1,32,221,1540,48,v=>{Workbook.researchObject=v-1;SaveWorkbook();});
                Choice(panel,new[]{"请选择起点与终点","从领取配套物料到成品入库","从开灯到关灯","从采购到销售"},Workbook.researchBoundary+1,32,288,1540,48,v=>{Workbook.researchBoundary=v-1;SaveWorkbook();});
                Choice(panel,new[]{"请选择约束","保留检验、人数、工序和工时","允许取消检验","允许增加人员"},Workbook.constraints+1,32,355,1540,48,v=>{Workbook.constraints=v-1;SaveWorkbook();});
                Text(panel,"用自己的话写出研究边界与改善目标（至少 12 字）",32,434,1500,40,23);
                Entry(panel,Workbook.boundary,32,488,1540,172,v=>Workbook.boundary=v);
            }
            if(s==1)
            {
                Text(panel,"组件汇入：底座 + 灯杆 → 装配 ← 灯头模块；两项装配合计 30 秒。\n工艺程序图只记录加工与检查。请为每个节点分类。",32,155,1530,76,22);
                for(int i=0;i<5;i++){int index=i;Text(panel,(i+1)+". "+FactoryLesson.ChartLabels[i],32,260+i*78,1020,48,24);Choice(panel,FactoryLesson.Types,Workbook.choices[i]+1,1090,252+i*78,480,48,v=>{Workbook.choices[index]=v-1;SaveWorkbook();});}
            }
            if(s==2)
            {
                Text(panel,"保持单件研究对象，判断五类活动；选中产品的真实时间和距离在报告中自动列出。",32,153,1540,40,22);
                for(int i=0;i<10;i++){int index=i;int col=i/5,row=i%5;Text(panel,(i+1)+". "+FactoryLesson.FlowLabels[i],32+col*790,220+row*96,735,35,21);Choice(panel,FactoryLesson.Types,Workbook.choices[5+i]+1,32+col*790,258+row*96,735,42,v=>{Workbook.choices[5+index]=v-1;SaveWorkbook();});}
            }
            if(s==3)
            {
                Text(panel,"从原始 20 件订单中选取两条事实，再用 5W1H 分析。",32,158,1530,38,23);
                EvidenceChoice(panel,"搬运证据", "transport",Workbook.routeEvidence,215,v=>Workbook.routeEvidence=v);
                EvidenceChoice(panel,"等待证据", "delay",Workbook.waitEvidence,322,v=>Workbook.waitEvidence=v);
                Text(panel,"何事 / 为何 / 何处 / 何时 / 何人 / 如何（至少 20 字）",32,434,1540,36,22);
                Entry(panel,Workbook.analysis,32,481,1540,210,v=>Workbook.analysis=v);
            }
            if(s==4)
            {
                Text(panel,"先提交预测，再运行改善订单。当前可执行改善为交换工位，工艺顺序与检验保持不变。",32,155,1540,64,22);
                Choice(panel,new[]{"选择 ECRS","取消 E","合并 C","重排 R","简化 S"},Workbook.ecrs+1,32,224,460,46,v=>{Workbook.ecrs=v-1;SaveWorkbook();});
                Text(panel,"改善方案及理由（至少 12 字）",32,294,1540,35,22);Entry(panel,Workbook.proposal,32,337,1540,135,v=>Workbook.proposal=v);
                Text(panel,"运行前预测：距离、等待与完成时间可能怎样变化？（至少 12 字）",32,493,1540,35,22);Entry(panel,Workbook.prediction,32,536,1540,135,v=>Workbook.prediction=v);
            }
            if(s==5)
            {
                Text(panel,FactoryRun.Compare(Baseline,Improved),32,158,720,415,21);
                Text(panel,"依据数据解释有效 / 无效之处（至少 20 字）",790,160,795,62,22);Entry(panel,Workbook.conclusion,790,226,780,183,v=>Workbook.conclusion=v);
                Text(panel,"维持方案与下一轮验证（至少 12 字）",790,434,795,42,22);Entry(panel,Workbook.maintain,790,480,780,176,v=>Workbook.maintain=v);
                Text(panel,"客观分 "+Workbook.ObjectiveScore+" / 40；主观 60 分待教师复核，非最终总分。",32,675,1540,38,22);
            }
            Button(panel,"逐级提示",32,740,215,46,()=>{modalStatus.text="提示 "+(Math.Min(hintLevel,2)+1)+"："+FactoryLesson.Hint(s,hintLevel);hintLevel++;});
            Button(panel,"提交本步",273,740,215,46,()=>{string error=FactoryLesson.ValidateStep(Workbook,s,Baseline,Improved);if(error.Length>0){modalStatus.text=error;return;}Workbook.submitted[s]=true;Workbook.baselineId=Baseline?.id??"";Workbook.improvedId=Improved?.id??"";SaveWorkbook();modalStatus.text="已提交。客观分 "+Workbook.ObjectiveScore+" / 40；分析、方案和结论待教师复核。";});
            Button(panel,"导出 HTML 记录",514,740,300,46,ExportReport);
            if(s<5)Button(panel,"下一步",838,740,215,46,()=>OpenLesson(s+1));
            Button(panel,"新实训（归档当前）",1110,740,460,46,NewWorkbook);
        }
        private void NewWorkbook()
        {
            ExportReport();
            Workbook=new FactoryWorkbook();Baseline=null;Improved=null;SaveWorkbook();
            try{Directory.CreateDirectory(CourseDirectory);File.WriteAllText(Path.Combine(CourseDirectory,"comparison.json"),"{\"baseline\":null,\"improved\":null}");}
            catch(Exception e){SetMessage("新实训结果保存失败："+e.Message);}
            RestoreBaseline();ResetExperiment(20);OpenLesson(0);
        }
        private void EvidenceChoice(Transform panel,string title,string activity,string selected,float y,Action<string> choose)
        {
            Text(panel,title,32,y,1540,33,22);
            var evidence=Baseline?.events.Where(e=>e.activity==activity && e.subject=="LAMP-01" && e.endSeconds>e.startSeconds).Select(e=>e.subject+" | "+e.startSeconds+"–"+e.endSeconds+" s | "+e.from+" → "+e.to+" | "+e.distanceM+" m | "+ReasonName(e.reason)).ToArray()??Array.Empty<string>();
            var list=new[]{"请选择实际事件"}.Concat(evidence).ToArray();int value=Array.IndexOf(list,selected);Choice(panel,list,Math.Max(0,value),32,y+40,1540,46,v=>{choose(v==0?"":list[v]);SaveWorkbook();});
        }
        public void ExportReport()
        {
            if (firstPersonCourse) { ExportWalkReport(); return; }
            try
            {
                string folder=Path.Combine(CourseDirectory,"Reports");Directory.CreateDirectory(folder);
                LastReportPath=Path.Combine(folder,"FactoryLab-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".html");
                File.WriteAllText(LastReportPath,FactoryLesson.Report(Workbook,Baseline,Improved),new System.Text.UTF8Encoding(false));
                File.WriteAllText(Path.ChangeExtension(LastReportPath,"json"),JsonConvert.SerializeObject(new{workbook=Workbook,baseline=Baseline,improved=Improved},Formatting.Indented));
                if(modalStatus!=null)modalStatus.text="已导出实验记录："+LastReportPath;SetMessage("实验记录已导出："+LastReportPath);
            }
            catch(Exception e){if(modalStatus!=null)modalStatus.text="报告导出失败："+e.Message;}
        }
    }
}
