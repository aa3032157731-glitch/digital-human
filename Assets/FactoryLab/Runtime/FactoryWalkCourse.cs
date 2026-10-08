using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace FactoryLab
{
    public sealed partial class FactoryLabController
    {
        public FactoryProgressV2 Progress { get; private set; } = new FactoryProgressV2();
        private FactoryFirstPerson walker;
        private Text walkTask, walkPrompt, walkStats;
        private GameObject resumeControl;
        private Transform teacherPoint;
        private TextMesh[] signs;
        private Material worldTextMaterial;
        private int selectedSwap = -1, questionIndex;
        private bool swapMode, movingTeacher;
        private string walkFeedback = "";
        private CancellationTokenSource lessonSpeech;
        private string V2Directory => Path.Combine(StorageOverride ?? Path.Combine(Application.persistentDataPath,"FactoryLab"),"Course-v2");

        private void InitializeWalkCourse()
        {
            SetSpeed(5);
            bool restored=false;
            try
            {
                string path=Path.Combine(V2Directory,"progress.json");
                if(Application.isPlaying && File.Exists(path))
                {
                    var saved=JsonConvert.DeserializeObject<FactoryProgressV2>(File.ReadAllText(path));
                    if(saved==null || saved.version!=2 || saved.step<0 || saved.step>5 || saved.visited?.Length!=6 || saved.completed?.Length!=6 || saved.answers==null || saved.predictions==null || saved.answers.Any(a=>a==null || a.attempts==null || a.attempts.Any(t=>t==null) || !FactoryQuestions.All.Any(q=>q.id==a.questionId) || double.IsNaN(a.expected) || double.IsInfinity(a.expected)) || saved.answers.Select(a=>a.questionId).Distinct().Count()!=saved.answers.Count || saved.predictions.Any(p=>p==null || p.trends==null || p.trends.Length!=3 || p.trends.Any(t=>t < -1 || t > 2)))
                        throw new InvalidDataException("课程存档结构不正确。");
                    if(saved.layoutJson!=null)FactoryConfiguration.Read(caseData.text,saved.layoutJson,"student-layout");
                    Progress=saved; currentLayout=saved.layoutJson; Baseline=saved.baseline; Improved=saved.improved;
                    restored=true;
                }
            }
            catch(Exception e) { walkFeedback="存档未能恢复："+e.Message+" 已开始新课程。"; Progress=new FactoryProgressV2(); }
            foreach(Transform child in Hud.transform)child.gameObject.SetActive(false);
            var player=new GameObject("First person student",typeof(CharacterController),typeof(FactoryFirstPerson));
            player.transform.SetParent(transform); walker=player.GetComponent<FactoryFirstPerson>();
            walker.Configure(sceneCamera); walker.Teleport(new Vector3(.65f,.1f,4f)); player.transform.rotation=Quaternion.Euler(0,90,0);
            walker.pause=OpenWalkPause; walker.task=OpenWalkTask;
            sceneCamera.cullingMask |= 1<<30;
            if(teacherCamera!=null)teacherCamera.enabled=false;
            RebuildLayout();
            walkTask=Text(Hud.transform,"",30,28,1000,104,25,Color.white);
            var backing=Panel(Hud.transform,"Task background",20,18,1040,123,new Color(.035f,.09f,.13f,.85f)); backing.SetAsFirstSibling();
            walkPrompt=Text(Hud.transform,"",550,880,1000,85,26,Color.white); walkPrompt.alignment=TextAnchor.MiddleCenter;
            Panel(Hud.transform,"Status background",20,975,1860,94,new Color(.035f,.09f,.13f,.85f));
            walkStats=Text(Hud.transform,"",32,984,1750,65,21,Color.white);
            var cross=Text(Hud.transform,"+",945,519,30,42,24,Color.white); cross.alignment=TextAnchor.MiddleCenter;
            resumeControl=Button(Hud.transform,"继续行走 / 手动继续仿真",650,800,620,56,ResumeWalk).gameObject;
            if(restored)walkFeedback="已恢复到安全教学检查点。未完成的订单已重置，请在控制台重新运行；已完成结果与成绩保留。";
            OpenWalkPause();
        }
        private void SaveWalkProgress()
        {
            if(!Application.isPlaying)return;
            try
            {
                Progress.layoutJson=currentLayout; Progress.baseline=Baseline; Progress.improved=Improved;
                Directory.CreateDirectory(V2Directory);
                string target=Path.Combine(V2Directory,"progress.json"), temporary=target+".tmp";
                File.WriteAllText(temporary,JsonConvert.SerializeObject(Progress,Formatting.Indented));
                if(File.Exists(target))File.Replace(temporary,target,target+".bak");else File.Move(temporary,target);
            }
            catch(Exception e) { walkFeedback="保存失败："+e.Message; }
        }
        private void CaptureWalkResult(FactoryRun run)
        {
            Progress.baseline=Baseline; Progress.improved=Improved;
            SaveWalkProgress(); walkFeedback="订单完成，实际事件和结果已保存。请回到老师处继续。";
        }
        private void PrepareWalkGeometry()
        {
            var geometry=transform.Find("Generated factory");
            foreach(var collider in geometry.GetComponentsInChildren<Collider>(true))
                collider.enabled=collider.name=="Floor 12m x 8m" || collider.name=="Back wall" || collider.name=="Left wall";
            AddBoundary(geometry,"Front boundary",new Vector3(6,1,-.08f),new Vector3(12,2,.12f));
            AddBoundary(geometry,"Right boundary",new Vector3(12.08f,1,4),new Vector3(.12f,2,8));
            if(worldTextMaterial==null)worldTextMaterial=new Material(Resources.Load<Shader>("FactoryWorldText"));
            worldTextMaterial.mainTexture=font.material.mainTexture;
            foreach(var oldLabel in geometry.GetComponentsInChildren<TextMesh>())oldLabel.gameObject.SetActive(false);
            signs=new TextMesh[6];
            for(int i=0;i<6;i++)
            {
                int index=i; var root=views[i].transform;
                var collider=root.gameObject.AddComponent<BoxCollider>(); collider.center=new Vector3(0,.85f,0); collider.size=new Vector3(i==0||i==5 ? 2.2f:1.85f,1.7f,1f);
                var interact=root.gameObject.AddComponent<FactoryInteraction>(); interact.prompt="观察 "+Simulation.Config.Stations[i].Name+" / 查看事件"; interact.action=()=>InteractStation(index);
                float sign=root.position.z<4 ? 1:-1;
                var board=FactoryGeometry.Box(root,"Chinese station sign",new Vector3(0,1.45f,sign*.53f),new Vector3(2.3f,.45f,.04f),palette[1]);board.GetComponent<Collider>().enabled=false;
                var label=FactoryGeometry.Label(root,"",new Vector3(0,1.45f,sign*.58f),.016f,FactoryGeometry.Navy);
                label.font=font; label.GetComponent<Renderer>().sharedMaterial=worldTextMaterial; label.fontSize=56;
                label.transform.localRotation=Quaternion.Euler(0,sign>0?180:0,0); signs[i]=label;
            }
            foreach(var collider in dynamicRoot.GetComponentsInChildren<Collider>(true))collider.enabled=false;
            var console=FactoryGeometry.Box(geometry,"Order console",new Vector3(11.35f,.75f,3.9f),new Vector3(.5f,1.5f,.65f),palette[3]);
            var action=console.AddComponent<FactoryInteraction>();action.prompt="订单控制台";action.action=OpenWalkConsole;
            var labelConsole=FactoryGeometry.Label(console.transform,"订单控制台",new Vector3(0,1f,0),.04f,Color.white);
            labelConsole.font=font;labelConsole.GetComponent<Renderer>().sharedMaterial=worldTextMaterial;labelConsole.transform.rotation=Quaternion.Euler(0,90,0);
            var marker=FactoryGeometry.Empty(geometry,"Teacher interaction",Vector3.zero); teacherPoint=marker;
            var teacherCollider=marker.gameObject.AddComponent<CapsuleCollider>();teacherCollider.height=1.8f;teacherCollider.radius=.35f;teacherCollider.center=Vector3.up*.9f;
            var teacherAction=marker.gameObject.AddComponent<FactoryInteraction>();teacherAction.prompt="与老师对话";teacherAction.action=OpenWalkTeacher;
            PlaceTeacher();
        }
        private void AddBoundary(Transform parent,string name,Vector3 center,Vector3 size)
        {
            var wall=FactoryGeometry.Box(parent,name,center,size,palette[1]);
            wall.GetComponent<Renderer>().enabled=false;
        }
        private Vector3 TeacherDestination()
        {
            if(Progress.step==0)return new Vector3(2.5f,.06f,4.5f);
            int station=Progress.step==1 ? 1 : Progress.step==2 ? 3 : Progress.step==3 ? 2 : 4;
            var center=views[station].transform.position;
            return new Vector3(center.x-1.25f,.06f,center.z<4 ? 3.05f:4.95f);
        }
        private void PlaceTeacher()
        {
            if(teacherPoint==null)return;
            var destination=TeacherDestination();
            if(walker!=null && Vector3.Distance(destination,walker.transform.position)<.85f)walker.Teleport(new Vector3(.65f,.1f,4f));
            teacherPoint.position=destination;
            if(teacherRig==null)return;
            teacherRig.SetActive(true);
            if(!Application.isPlaying)foreach(var animator in teacherRig.GetComponentsInChildren<Animator>()){animator.Rebind();animator.Update(.1f);}
            foreach(var collider in teacherRig.GetComponentsInChildren<Collider>(true))collider.enabled=false;
            var renderers=teacherRig.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.gameObject.activeInHierarchy).ToArray();
            if(renderers.Length==0)return;
            var bounds=renderers[0].bounds; foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            teacherRig.transform.position+=destination-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
        }
        private void UpdateWalkCourse()
        {
            if(walker==null)return;
            worldTextMaterial.mainTexture=font.material.mainTexture;
            Vector3 targetPoint=teacherPoint.position;
            string next="老师";
            if(Progress.accepted && !Progress.Ready)
            {
                int station=Progress.step==0 ? (Progress.visited[0]?-1:0) : Progress.step==1 ? Enumerable.Range(1,4).Where(i=>!Progress.visited[i]).DefaultIfEmpty(-1).First() : -1;
                if(station>=0) { targetPoint=views[station].transform.position; next=Simulation.Config.Stations[station].Name; }
                else if((Progress.step==2 && (Baseline==null || !Progress.ledgerViewed)) || (Progress.step==5 && Improved==null)) { targetPoint=new Vector3(11.35f,0,3.9f);next="订单控制台"; }
                else if(Progress.step==4 && Progress.swaps==0){targetPoint=views[selectedSwap<0?1:2].transform.position;next="选择两个工位";}
            }
            var direction=targetPoint-walker.transform.position;
            float angle=Vector3.SignedAngle(walker.transform.forward,direction,Vector3.up);
            string arrow=Mathf.Abs(angle)<25?"↑":Mathf.Abs(angle)>150?"↓":angle>0?"→":"←";
            walkTask.text=$"{Progress.step+1}/6  {FactoryQuestions.Steps[Progress.step]}    首次 {Progress.FirstScore} / 掌握 {Progress.MasteryScore}\n{arrow} {next} · {direction.magnitude:0.0} m    Tab 任务  |  Esc 暂停";
            walkPrompt.text=walker.Prompt;
            walkStats.text=$"WASD 行走  ·  鼠标转头  ·  E 交互    {Simulation.Time:0.0} s   完成 {Simulation.FinishedCount}/20   {Speed}×\n{walkFeedback}";
            for(int i=0;i<6;i++) signs[i].text=$"{i+1:00}  {Simulation.Config.Stations[i].Name}\n{(Simulation.Stations[i].Processing!=null?"作业中":Simulation.Stations[i].Output!=null?"待搬运":"待料 / 空闲")}  · E 事件";
            if(teacherRig!=null && teacherPoint!=null)
            {
                var look=walker.transform.position-teacherPoint.position;look.y=0;
                // Imported character faces local -Z. Rotate around the teaching anchor, not its source-scene pivot.
                if(look.sqrMagnitude>.1f)
                {
                    float desired=Quaternion.LookRotation(-look).eulerAngles.y;
                    float delta=Mathf.DeltaAngle(teacherRig.transform.eulerAngles.y,desired);
                    teacherRig.transform.RotateAround(teacherPoint.position,Vector3.up,delta);
                }
            }
        }
        private void ShowResume() { if(resumeControl!=null)resumeControl.SetActive(true); CancelLessonSpeech(); }
        private void ResumeWalk()
        {
            if(movingTeacher)return;
            CloseModal();resumeControl.SetActive(false);walker.SetWalking(true);
            if(Simulation.Time>0 && !Simulation.Complete)StartWalkExperiment();
        }
        private void OpenWalkPause()
        {
            if(walker==null)return;
            var panel=Modal("第一人称实训 · 暂停与设置");
            Text(panel,"WASD 行走 · 鼠标转头 · E 交互 · Tab 任务 · Esc 释放鼠标\n\n老师在现场等待。接受任务后，走到工位旁完成观察。\n对话、答题和交换时仿真暂停，点击继续后才会恢复。\n\n"+walkFeedback,40,130,1520,270,26);
            Text(panel,"鼠标灵敏度",40,430,450,45,24);
            Choice(panel,new[]{"低 · 1","标准 · 2","高 · 3"},Mathf.RoundToInt(walker.sensitivity)-1,520,430,460,50,v=>walker.sensitivity=v+1);
            Button(panel,"继续行走",40,600,430,65,ResumeWalk);
            Button(panel,"查看任务",510,600,430,65,OpenWalkTask);
            Button(panel,"导出报告",980,600,430,65,ExportWalkReport);
            Button(panel,"复习已完成内容",40,695,680,60,()=>OpenWalkReview(0));
            Button(panel,"新实训（先归档本次）",770,695,640,60,ConfirmNewWalkCourse);
        }
        private void OpenWalkTeacher()
        {
            var panel=Modal("老师带教 · "+FactoryQuestions.Steps[Progress.step]);
            string script=Progress.complete?$"课程完成 · 首次 {Progress.FirstScore}/100 · 掌握 {Progress.MasteryScore}/100\n载货 {Baseline.loaded:0.0} → {Improved.loaded:0.0} m\n等待 {Baseline.waiting:0.0} → {Improved.waiting:0.0} s\n订单时间 {Baseline.elapsed:0.0} → {Improved.elapsed:0.0} s":FactoryQuestions.Instructions[Progress.step];
            Text(panel,script,40,135,1530,180,30);
            Text(panel,Progress.complete?"全部环节已完成，可复习内容或导出报告。":Progress.Ready?"观察、操作和纠错已完成。确认后前往下一教学站。":Progress.Gate(),40,340,1500,100,25,Accent);
            Button(panel,Progress.accepted?"查看任务 / 答题":"我准备好了",40,500,480,60,()=>{Progress.accepted=true;SaveWalkProgress();OpenWalkTask();});
            Button(panel,"再解释一下",550,500,480,60,()=>{modalStatus.text=script;Narrate(script);});
            Button(panel,"我该去哪里",1060,500,480,60,()=>modalStatus.text=Progress.Gate().Length>0?Progress.Gate():"查看任务，逐题答对后回到老师处确认下一站。");
            Button(panel,"自由文字 / 语音提问",40,600,480,60,OpenTeacher);
            var next=Button(panel,Progress.step==5?"完成课程":"确认前往下一站",550,600,480,60,CompleteWalkStep);next.interactable=Progress.Ready && !Progress.complete;
            Button(panel,"导出学习报告",1060,600,480,60,ExportWalkReport);
            Narrate(script);
        }
        private async void Narrate(string text)
        {
            CancelLessonSpeech();
            if(teacherVoice==null || !teacherVoice.enabled || !Application.isPlaying)return;
            var request=new CancellationTokenSource();lessonSpeech=request;
            try { await teacherVoice.SpeakTeachingTextAsync(text,request.Token); }
            catch(OperationCanceledException) { }
            catch(Exception) { if(lessonSpeech==request && modalStatus!=null)modalStatus.text="朗读服务不可用，文字讲解可继续学习。"; }
            finally { if(lessonSpeech==request)lessonSpeech=null;request.Dispose(); }
        }
        private void CancelLessonSpeech() { lessonSpeech?.Cancel();lessonSpeech=null; }
        private void CompleteWalkStep()
        {
            if(!Progress.Ready || Progress.complete)return;
            Progress.completed[Progress.step]=true;
            if(Progress.step==5) { Progress.complete=true;SaveWalkProgress();OpenWalkTeacher();return; }
            Progress.step++;Progress.accepted=false;questionIndex=0;swapMode=false;selectedSwap=-1;
            InvalidateTeacher();SaveWalkProgress();
            if(Application.isPlaying)StartCoroutine(TravelTeacher());else {PlaceTeacher();OpenWalkTeacher();}
        }
        private IEnumerator TravelTeacher()
        {
            movingTeacher=true;CloseModal();
            var fade=Panel(Hud.transform,"前往下一站",0,0,1920,1080,new Color(.02f,.05f,.08f,0));
            var image=fade.GetComponent<Image>();
            for(float t=0;t<.3f;t+=Time.unscaledDeltaTime){image.color=new Color(.02f,.05f,.08f,Mathf.Clamp01(t/.3f));yield return null;}
            PlaceTeacher();walkFeedback="老师已到下一站，请按方向提示前往。";
            for(float t=.3f;t>0;t-=Time.unscaledDeltaTime){image.color=new Color(.02f,.05f,.08f,Mathf.Clamp01(t/.3f));yield return null;}
            Destroy(fade.gameObject);movingTeacher=false;ShowResume();
        }
        private void InteractStation(int station)
        {
            if(swapMode && Progress.step==4 && Progress.accepted)
            {
                if(station==0 || station==5){walkFeedback="供料区和成品区固定，不能选择。";return;}
                if(selectedSwap<0){selectedSwap=station;walkFeedback="已选择 "+Simulation.Config.Stations[station].Name+"，请走到第二个作业工位按 E。";return;}
                if(selectedSwap==station){walkFeedback="请选择另一个工位。";return;}
                PreviewWalkSwap(selectedSwap,station);return;
            }
            if(Progress.accepted)Progress.visited[station]=true;
            SelectStation(station);SaveWalkProgress();
            var panel=Modal($"{station+1:00} · {Simulation.Config.Stations[station].Name}");
            Text(panel,$"当前设定工时：{Simulation.Config.Stations[station].Cycle:0.0} s\n{(station==3?"检查 □：确认外观、紧固和亮灯状态。":station==0||station==5?"储存 ▽：按规则受控保管。":"加工 ○：改变产品结构或完成包装。")}\n\n底座 + 灯杆 → 装配 ← 灯头模块\n装配 → 紧固 → 检验 → 包装\n\n装配子节点合计 30 秒。选择事件可查看单件产品的五类活动。",40,135,1500,370,27);
            Button(panel,"查看 LAMP-01 事件",40,560,680,65,()=>OpenWalkEvents(0));
            Button(panel,"查看任务",770,560,680,65,OpenWalkTask);
        }
        private void OpenWalkConsole()
        {
            var panel=Modal("订单控制台 · 20 件同参数实验");
            Text(panel,$"布局版本 {Progress.layoutVersion} · {(IsBaseline?"原始布局":"学生调整布局")}\n订单 {Simulation.Time:0.0} s · 完成 {Simulation.FinishedCount}/20\n单件完整载货路线 {CurrentConfiguration().ProductDistance():0.0} m\n\n"+FactoryRun.Compare(Baseline,Improved),40,125,1510,400,25);
            Button(panel,"开始 / 继续订单",40,555,450,60,()=>{CloseModal();StartWalkExperiment();if(Running){resumeControl.SetActive(false);walker.SetWalking(true);}});
            Button(panel,"查看产品事件",535,555,450,60,()=>OpenWalkEvents(0));
            Choice(panel,new[]{"1× 实时","5× 加速","10× 加速"},Speed==1?0:Speed==5?1:2,1030,555,450,60,v=>SetSpeed(new[]{1,5,10}[v]));
            Button(panel,"查看任务",40,650,450,60,OpenWalkTask);
        }
        private void StartWalkExperiment()
        {
            if(!Progress.accepted || (IsBaseline ? Progress.step!=2 : Progress.step!=5)) { walkFeedback="请先由老师布置本阶段实验任务。";return; }
            if(IsBaseline && Baseline!=null) {walkFeedback="原始结果已保留，请回到老师处答题。";return;}
            if(!IsBaseline)
            {
                var prediction=Progress.Prediction;
                if(prediction==null || prediction.trends.Any(t=>t<0)){walkFeedback="请先完成三个趋势预测。";return;}
                prediction.locked=true;prediction.runId=ExperimentId;SaveWalkProgress();
            }
            if(dynamicRoot!=null)foreach(var collider in dynamicRoot.GetComponentsInChildren<Collider>(true))collider.enabled=false;
            if(!Simulation.Complete) {Running=true;walkFeedback="订单运行中，请观察 LAMP-01；对话时会暂停。";}
        }
        private void OpenWalkEvents(int page)
        {
            if(Progress.step==2 && Baseline!=null){Progress.ledgerViewed=true;SaveWalkProgress();}
            var panel=Modal("LAMP-01 · 实际流程事件");
            var events=(Baseline!=null && Progress.step<=4?Baseline.events:Simulation.Events.ToArray()).Where(e=>e.subject=="LAMP-01").ToArray();
            int pages=Math.Max(1,(events.Length+5)/6);page=Mathf.Clamp(page,0,pages-1);
            Text(panel,"加工 ○ / 检查 □ / 搬运 → / 等待 D / 储存 ▽     ·     "+(page+1)+" / "+pages,40,115,1500,45,25);
            int row=0;
            foreach(var e in events.Skip(page*6).Take(6))
            {
                string evidence=$"LAMP-01 | {ActivityName(e.activity)} | {e.startSeconds:0.0} → {(e.endSeconds.HasValue?e.endSeconds.Value.ToString("0.0"):"未结束")} s | {e.distanceM:0.0} m | {e.from} → {e.to} | {ReasonName(e.reason)}";
                Text(panel,evidence,40,185+row*83,1160,75,21);
                if(Progress.step==3 && Progress.accepted && (e.activity=="transport" || e.activity=="delay") && e.endSeconds>e.startSeconds)
                {
                    string activity=e.activity;
                    Button(panel,"选择证据",1240,185+row*83,290,58,()=>{if(activity=="transport")Progress.transportEvidence=evidence;else Progress.waitingEvidence=evidence;SaveWalkProgress();modalStatus.text="已选取："+evidence;});
                }
                row++;
            }
            int currentPage=page;
            Button(panel,"上一页",40,705,300,55,()=>OpenWalkEvents(currentPage-1));
            Button(panel,"下一页",380,705,300,55,()=>OpenWalkEvents(currentPage+1));
            Button(panel,"查看任务",730,705,400,55,OpenWalkTask);
        }
        private void PreviewWalkSwap(int first,int second)
        {
            var panel=Modal("现场交换预览 · 确认后重置当前订单");
            string description=$"{Simulation.Config.Stations[first].Name}：{views[first].transform.position} → {views[second].transform.position}\n\n{Simulation.Config.Stations[second].Name}：{views[second].transform.position} → {views[first].transform.position}";
            Text(panel,description+"\n\n保留原始订单结果。确认后重置当前订单；为避免角色被覆盖，学生回到入口安全观察点，老师移至当前教学点。\n\n工艺、工时、人数及检验不变。",40,145,1510,310,27);
            // A top-down diagram makes the positional preview readable without moving the player camera.
            for(int i=0;i<6;i++)
            {
                var pos=views[i==first?second:i==second?first:i].transform.position;
                var tile=Panel(panel,"Preview station",90+pos.x*90,470+(8-pos.z)*22,250,58,i==first||i==second?new Color(.72f,.9f,.87f):Color.white);
                Text(tile,Simulation.Config.Stations[i].Name,10,9,230,42,22);
            }
            Button(panel,"确认重置并交换",80,720,650,60,()=>ConfirmWalkSwap(first,second));
            Button(panel,"取消交换",800,720,650,60,()=>{selectedSwap=-1;ResumeWalk();});
        }
        private void ConfirmWalkSwap(int first,int second)
        {
            if(Progress.step!=4 || !Progress.accepted || Progress.answers.Any(a=>a.questionId=="q17" && a.bound))return;
            PauseExperiment();ResetExperiment(20);walker.Teleport(new Vector3(.65f,.1f,4f));
            SwapStations(first,second);Progress.layoutVersion++;Progress.swaps++;Improved=null;Progress.improved=null;
            Progress.predictions.Add(new FactoryPrediction{layoutVersion=Progress.layoutVersion});
            selectedSwap=-1;swapMode=false;PlaceTeacher();SaveWalkProgress();OpenWalkTask();
        }
        private void OpenWalkTask()
        {
            if(walker==null)return;
            var panel=Modal("实训任务 · "+FactoryQuestions.Steps[Progress.step]);
            Text(panel,FactoryQuestions.Instructions[Progress.step],40,112,1510,115,25);
            string gate=Progress.Gate();
            Text(panel,$"首次分 {Progress.FirstScore}/100   掌握分 {Progress.MasteryScore}/100    "+(gate.Length==0?"现场条件已满足，可以逐题作答。":gate),40,230,1510,85,24,Accent);
            if(Progress.step==3)
            {
                Button(panel,"选择实际搬运 / 等待事件",40,340,1470,60,()=>OpenWalkEvents(0));
                Text(panel,(Progress.transportEvidence??"未选择搬运证据")+"\n"+(Progress.waitingEvidence??"未选择等待证据"),40,420,1500,160,23);
            }
            if(Progress.step==4)
            {
                bool frozen=Progress.Record("q17").bound || Progress.Prediction?.locked==true;
                var swap=Button(panel,"现场选择两个工位",40,332,650,60,()=>{swapMode=true;selectedSwap=-1;walkFeedback="靠近第一个作业工位按 E。";ResumeWalk();});swap.interactable=Progress.accepted&&!frozen;
                Text(panel,$"布局版本 {Progress.layoutVersion} · 当前单件路线 {CurrentConfiguration().ProductDistance():0.0} m",750,345,800,50,24);
                if(Progress.Prediction!=null)
                {
                    string[] labels={"距离","等待","完成时间"};
                    for(int i=0;i<3;i++)
                    {
                        int index=i;Text(panel,labels[i],40+i*510,435,460,45,24);
                        var choice=Choice(panel,new[]{"请选择趋势","减少","不变","增加"},Progress.Prediction.trends[i]+1,40+i*510,490,460,55,v=>{Progress.Prediction.trends[index]=v-1;SaveWalkProgress();});choice.interactable=!Progress.Prediction.locked;
                    }
                    Text(panel,"预测不按命中扣分；开跑后锁定。数字题绑定本布局后停止继续交换。",40,585,1510,65,22);
                }
            }
            if(Progress.step!=3 && Progress.step!=4)Text(panel,"可自由走动和复习工位。当前环节的现场任务及全部题目纠错完成后，回到老师处确认前往下一站。",40,360,1500,170,27);
            Button(panel,"开始 / 继续逐题作答",40,705,690,60,()=>{if(Progress.Gate().Length>0){modalStatus.text=Progress.Gate();return;}questionIndex=0;OpenWalkQuestion();});
            Button(panel,"返回现场",800,705,690,60,ResumeWalk);
        }
        private void OpenWalkQuestion()
        {
            var questions=FactoryQuestions.All.Where(q=>q.step==Progress.step).ToArray();
            questionIndex=Mathf.Clamp(questionIndex,0,questions.Length-1);
            var q=questions[questionIndex];var record=Progress.Record(q.id);
            q.Bind(record,Progress,CurrentConfiguration().ProductDistance());SaveWalkProgress();
            var panel=Modal($"{FactoryQuestions.Steps[Progress.step]} · 第 {questionIndex+1}/{questions.Length} 题 · 5 分");
            Text(panel,q.text+(q.Numeric?"（单位 "+q.unit+"，误差 ±0.1）":""),40,135,1500,100,30);
            Text(panel,"现场证据："+record.evidence,40,240,1500,110,23,Muted);
            if(q.Numeric)
            {
                var input=Entry(panel,"",40,380,1030,70,_=>{});input.lineType=InputField.LineType.SingleLine;input.characterLimit=30;
                Button(panel,"提交数字",1120,380,390,70,()=>SubmitWalkQuestion(q,record,input.text));
            }
            else
            {
                for(int i=0;i<q.options.Length;i++){int choice=i;var b=Button(panel,q.options[i],40,355+i*70,1470,60,()=>SubmitWalkQuestion(q,record,choice.ToString()));b.interactable=record.MasteryScore==0;}
            }
            Text(panel,$"首次 {record.FirstScore}/5 · 掌握 {record.MasteryScore}/5 · 尝试 {record.attempts.Count} 次 · 提示 {record.hints} 次",40,655,1510,40,23);
            Button(panel,"老师提示",40,715,410,60,()=>{record.hints++;SaveWalkProgress();modalStatus.text=q.explanation;Narrate(q.explanation);});
            Button(panel,"下一题 / 返回任务",510,715,520,60,()=>{if(record.MasteryScore!=5){modalStatus.text="请先纠正当前题目。";return;}if(questionIndex<questions.Length-1){questionIndex++;OpenWalkQuestion();}else OpenWalkTask();});
            Button(panel,"复习上一题",1090,715,420,60,()=>{questionIndex=Math.Max(0,questionIndex-1);OpenWalkQuestion();});
            if(record.MasteryScore==5)modalStatus.text="已掌握："+q.explanation;
        }
        private void SubmitWalkQuestion(FactoryQuestion q,FactoryAnswer record,string answer)
        {
            bool correct=q.Submit(record,answer);SaveWalkProgress();OpenWalkQuestion();
            modalStatus.text=(correct?"回答正确。":"暂不正确，可以重答。")+q.explanation;Narrate(modalStatus.text);
        }
        private void OpenWalkReview(int step)
        {
            var panel=Modal("复习已完成内容");
            var completed=Enumerable.Range(0,6).Where(i=>Progress.completed[i]).ToArray();
            if(completed.Length==0){Text(panel,"尚无已完成环节。请到老师处接受当前任务。",40,150,1500,150,28);return;}
            if(!completed.Contains(step))step=completed[0];
            Choice(panel,completed.Select(i=>FactoryQuestions.Steps[i]).ToArray(),Array.IndexOf(completed,step),40,110,1470,55,v=>OpenWalkReview(completed[v]));
            int row=0;
            foreach(var q in FactoryQuestions.All.Where(q=>q.step==step))
            {
                var r=Progress.Record(q.id);
                Text(panel,q.text+"\n"+q.explanation+"\n首次 "+r.FirstScore+" / 掌握 "+r.MasteryScore+"；证据："+r.evidence,40,195+row*132,1500,124,23);row++;
            }
        }
        private void ConfirmNewWalkCourse()
        {
            var panel=Modal("新实训 · 归档当前记录");
            Text(panel,$"当前首次分 {Progress.FirstScore}/100，掌握分 {Progress.MasteryScore}/100。\n\n确认后导出 HTML 和 JSON 记录，再从入口开始新课程。旧版报告及本次归档均保留。",40,150,1500,260,28);
            Button(panel,"归档并开始新实训",40,540,1470,65,()=>
            {
                string previous=LastReportPath;ExportWalkReport();if(previous==LastReportPath)return;
                Progress=new FactoryProgressV2();Baseline=null;Improved=null;currentLayout=null;selectedSwap=-1;swapMode=false;questionIndex=0;
                walker.Teleport(new Vector3(.65f,.1f,4f));RebuildLayout();SaveWalkProgress();walkFeedback="新课程已开始，请前往入口老师处。";OpenWalkPause();
            });
        }
        private void ExportWalkReport()
        {
            try
            {
                SaveWalkProgress();Directory.CreateDirectory(Path.Combine(V2Directory,"Reports"));
                LastReportPath=Path.Combine(V2Directory,"Reports","FactoryLab-v2-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".html");
                File.WriteAllText(LastReportPath,FactoryQuestions.Report(Progress));
                File.WriteAllText(Path.ChangeExtension(LastReportPath,"json"),JsonConvert.SerializeObject(Progress,Formatting.Indented));
                walkFeedback="报告已导出。点击窗口右上角“记录目录”，在 Reports 文件夹中查看。";if(modalStatus!=null)modalStatus.text=walkFeedback;
            }
            catch(Exception e){walkFeedback="导出失败："+e.Message;if(modalStatus!=null)modalStatus.text=walkFeedback;}
        }
        private void OnApplicationQuit() { if(firstPersonCourse)SaveWalkProgress();CancelLessonSpeech(); }
    }
}
