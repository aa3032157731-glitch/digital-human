using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace FactoryLab
{
    public static class FactoryAcceptance
    {
        public static List<string> Run(FactoryLabController c)
        {
            var checks=new List<string>();
            Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("FACTORY ACCEPTANCE: "+name);checks.Add(name);};
            c.RestoreBaseline(); c.ResetExperiment(20); c.Simulation.Advance(10000); c.SaveCompletedRun();
            check(c.Baseline!=null && c.Baseline.elapsed==1771,"original 20-piece order saved");
            var baselineJson=JsonConvert.SerializeObject(c.Baseline);
            c.ResetExperiment(20);bool rejected=false;try{c.SwapStations(0,1);}catch(ArgumentException){rejected=true;}check(rejected,"fixed area cannot move");
            c.SwapStations(1,3);c.SwapStations(2,3);c.SwapStations(3,4);
            check(c.Simulation.Config.ProductDistance()==18,"swaps recalculate legal route");
            c.Simulation.Advance(10000);c.SaveCompletedRun();
            check(c.Improved.elapsed==1454 && c.Improved.loaded==360,"improved order saved");
            check(FactoryRun.Comparable(c.Baseline,c.Improved),"same parameter comparison");
            check(JsonConvert.SerializeObject(c.Baseline)==baselineJson,"baseline snapshot immutable after swaps");
            c.ResetExperiment(1);c.Simulation.Advance(10000);var other=FactoryRun.Capture(c.Simulation,"other");
            check(!FactoryRun.Comparable(c.Baseline,other),"different order quantities cannot compare");
            c.ResetExperiment(20);c.Simulation.Advance(51);string oldId=c.ExperimentId;
            foreach(var question in FactoryTeaching.Questions)
            {
                string answer=c.AskLocal(question);check(answer.Contains("快照")&&answer.Contains("LAMP-01"),"grounded local question: "+question);
            }
            c.CaptureTeacherContext("当前状态");check(c.LastTeacherContext.Contains(oldId)&&!c.Running,"question pauses and captures experiment id");
            c.ResetExperiment(20);c.CaptureTeacherContext("新实验");check(!c.LastTeacherContext.Contains(oldId)&&c.LastTeacherContext.Contains(c.ExperimentId),"reset replaces teacher snapshot");
            check(Math.Abs(Time.timeScale-1)<.0001,"independent simulation time scale");
            var book=c.Workbook;
            book.researchObject=book.researchBoundary=book.constraints=0;
            book.boundary="跟踪单件台灯，从领取物料到成品入库，保持人员工时工序不变且保留检验。";
            for(int i=0;i<5;i++)book.choices[i]=FactoryLesson.ChartAnswers[i];for(int i=0;i<10;i++)book.choices[i+5]=FactoryLesson.FlowAnswers[i];
            book.routeEvidence=c.Baseline.events.First(e=>e.activity=="transport").from;
            book.waitEvidence=c.Baseline.events.First(e=>e.activity=="delay").reason;
            book.analysis="何事：长距离搬运；何处：交接点通道；何人：搬运车；何时：实际事件；为何：布局折返；如何：沿中央通道。<script>alert(1)</script>";
            book.ecrs=2;book.proposal="交换工位重排布局，保持检验、工时和人数不变。";book.prediction="预计载货距离减少，但等待与订单时间需要分别验证。";
            book.conclusion="载货距离从680米降到360米，订单1771秒变为1454秒，但累计等待增加，不能认为所有指标改善。";
            book.maintain="保留布局版本，采集真实工厂工时并重复运行核对结果。";
            for(int i=0;i<6;i++){string error=FactoryLesson.ValidateStep(book,i,c.Baseline,c.Improved);check(error=="","lesson step "+(i+1));book.submitted[i]=true;}
            check(book.ObjectiveScore==40,"objective score bounded at 40; manual 60 pending");
            c.ExportReport();string report=File.ReadAllText(c.LastReportPath);
            check(report.Contains("&lt;script&gt;")&&!report.Contains("<script>"),"report escapes student text");
            check(report.Contains("1771")&&report.Contains("1454")&&report.Contains("LAMP-01"),"report includes actual comparison and product ledger");
            c.OpenLayout();c.CloseModal();for(int i=0;i<6;i++){c.OpenLesson(i);c.CloseModal();}
            c.OpenTeacher();check(c.teacherRig!=null&&c.teacherCamera!=null&&c.teacherVoice!=null,"existing digital human installed");
            c.AskLocal(FactoryTeaching.Questions[0]);c.CloseModal();
            c.RestoreBaseline();c.ResetExperiment(20);
            return checks;
        }
    }
    public sealed class FactoryPlayerAcceptance : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if(!Environment.GetCommandLineArgs().Contains("-factory-selftest"))return;
            Application.runInBackground=true;
            FactoryLabController.StorageOverride=Path.Combine(Application.persistentDataPath,"FactoryLab","Acceptance");
            DontDestroyOnLoad(new GameObject("Factory standalone acceptance",typeof(FactoryPlayerAcceptance)));
        }
        private IEnumerator Start()
        {
            for(int i=0;i<10;i++)yield return null;
            var c=FindObjectOfType<FactoryLabController>();var checks=new List<string>();string failure=null;
            try{checks=FactoryAcceptance.Run(c);}catch(Exception e){failure=e.ToString();Debug.LogException(e);}
            string root=FactoryLabController.StorageOverride;Directory.CreateDirectory(root);
            if(failure==null)
            {
                Debug.Log("FACTORY_PLAYER_LOGIC_PASS; starting GPU render checks");
                using(var capture=new FactoryOffscreenCapture(c,Screen.width,Screen.height))
                {
                    c.StartExperiment();for(int i=0;i<30;i++){capture.Draw();yield return null;}
                    float start=Time.realtimeSinceStartup;int frames=0;
                    while(Time.realtimeSinceStartup-start<8){capture.Draw();frames++;yield return null;}
                    double fps=frames/(Time.realtimeSinceStartup-start);c.PauseExperiment();capture.Save(Path.Combine(root,"standalone.png"));
                    c.OpenTeacher();c.AskLocal(FactoryTeaching.Questions[0]);
                    for(int i=0;i<60;i++){capture.Draw();yield return null;}
                    start=Time.realtimeSinceStartup;frames=0;
                    while(Time.realtimeSinceStartup-start<8){capture.Draw();frames++;yield return null;}
                    double teacherFps=frames/(Time.realtimeSinceStartup-start);capture.Save(Path.Combine(root,"standalone-teacher.png"));
                    var animators=c.teacherRig.GetComponentsInChildren<Animator>(true).Select(a=>new {a.name,a.enabled,controller=a.runtimeAnimatorController?.name,avatar=a.avatar?.name,state=a.GetCurrentAnimatorStateInfo(0).fullPathHash});
                    File.WriteAllText(Path.Combine(root,"player-result.json"),JsonConvert.SerializeObject(new{status="PASS",checks,renderMode="offscreen GPU with readback barrier; excludes desktop presentation",fps,teacherFps,width=Screen.width,height=Screen.height,report=c.LastReportPath,animators},Formatting.Indented));
                    Debug.Log("FACTORY_PLAYER_PASS rendered fps="+fps+" teacher="+teacherFps);
                }
                yield return null;Application.Quit(0);
            }
            else{File.WriteAllText(Path.Combine(root,"player-result.json"),JsonConvert.SerializeObject(new{status="FAIL",failure},Formatting.Indented));Application.Quit(1);}
        }
    }
}
