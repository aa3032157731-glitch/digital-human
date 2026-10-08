using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FactoryLab.Editor
{
    public static class FactoryV2Validation
    {
        private static readonly List<string> checks=new List<string>();
        private static void Check(bool ok,string text) { if(!ok)throw new Exception("V2: "+text);checks.Add(text); }
        private static void Call(FactoryLabController c,string method,params object[] args) => typeof(FactoryLabController).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c,args);
        public static void Validate()
        {
            checks.Clear();Directory.CreateDirectory(".work/factory-lab");
            Check(FactoryQuestions.All.Length==20,"20 questions");
            Check(FactoryQuestions.All.Count(q=>q.Numeric)==4,"4 numeric questions");
            Check(FactoryQuestions.All.Count(q=>!q.Numeric && q.options.Length==2)==6,"6 true/false questions");
            Check(FactoryQuestions.All.Count(q=>!q.Numeric && q.options.Length>2)==10,"10 multiple choice questions");
            Check(FactoryQuestions.All.All(q=>!string.IsNullOrWhiteSpace(q.explanation)),"all explanations present");
            var source=File.ReadAllText("Assets/FactoryLab/Data/factory-case.json");var layouts=File.ReadAllText("Assets/FactoryLab/Data/layouts.json");
            var baseline=new FactorySimulation(FactoryConfiguration.Read(source,layouts,"baseline"),20);baseline.Advance(10000);
            var improved=new FactorySimulation(FactoryConfiguration.Read(source,layouts,"reference-improved"),20);improved.Advance(10000);
            var p=new FactoryProgressV2{baseline=FactoryRun.Capture(baseline,"original"),improved=FactoryRun.Capture(improved,"adjusted")};
            Check(p.baseline.elapsed==1771 && p.improved.elapsed==1454,"20-piece regression 1771/1454");
            foreach(var q in FactoryQuestions.All)
            {
                var r=p.Record(q.id);q.Bind(r,p,18);
                Check(!q.Submit(r,(r.expected+2).ToString(CultureInfo.InvariantCulture)),q.id+" wrong first answer rejected");r.hints++;
                Check(q.Submit(r,r.expected.ToString(CultureInfo.InvariantCulture)),q.id+" retry correct");
                q.Submit(r,"bad duplicate");
                Check(r.FirstScore==0 && r.MasteryScore==5 && r.attempts.Count==2 && r.attempts[1].hintUsed,q.id+" first/mastery/hint/duplicate");
            }
            Check(p.FirstScore==0 && p.MasteryScore==100,"independent score totals");
            Check(p.Record("q11").expected==45 && p.Record("q14").expected==680 && p.Record("q17").expected==18 && p.Record("q20").expected==317,"four evidence bindings");
            var restored=JsonConvert.DeserializeObject<FactoryProgressV2>(JsonConvert.SerializeObject(p));
            Check(restored.MasteryScore==100 && restored.Record("q11").sourceId=="original","answer and snapshot round trip");
            var numeric=FactoryQuestions.All.Last();var negative=new FactoryProgressV2{baseline=p.improved,improved=p.baseline};var record=negative.Record(numeric.id);numeric.Bind(record,negative,34);
            Check(numeric.Submit(record,"-317.1"),"negative difference and tolerance boundary");
            foreach(string bad in new[]{"NaN","Infinity","-Infinity","","1e309","317.2"})
            {
                var a=new FactoryAnswer{bound=true,expected=317};Check(!numeric.Submit(a,bad),"reject invalid numeric "+bad);
            }
            Check(new FactoryProgressV2{step=5,accepted=true}.Gate().Length>0,"cannot skip missing experiment");
            EditorSceneManager.OpenScene(FactoryLabTools.ScenePath);
            var c=UnityEngine.Object.FindObjectOfType<FactoryLabController>();c.firstPersonCourse=true;
            FactoryLabController.StorageOverride=Path.GetFullPath(".work/factory-lab/v2-test");c.Initialize();
            var player=c.GetComponentInChildren<FactoryFirstPerson>();
            Check(player!=null && player.GetComponent<CharacterController>()!=null,"character controller initialized");
            Check(c.sceneCamera.rect==new Rect(0,0,1,1) && c.sceneCamera.fieldOfView==65,"full screen first person camera");
            Check(c.teacherRig!=null && c.teacherRig.activeSelf,"real digital human in scene");
            Check(c.views.All(v=>v.GetComponent<BoxCollider>()!=null && v.GetComponent<FactoryInteraction>()!=null),"all stations have solid bounds and interactions");
            for(int station=0;station<6;station++)
            {
                var center=c.views[station].transform.position;float side=center.z<4?1:-1;
                player.Teleport(center+new Vector3(0,.1f,side*1.4f));Physics.SyncTransforms();
                var direction=(center+Vector3.up*1.45f-player.view.transform.position).normalized;
                Check(Physics.Raycast(player.view.transform.position,direction,out var hit,2f) && hit.collider.GetComponentInParent<FactoryInteraction>()==c.views[station].GetComponent<FactoryInteraction>(),"eye-level ray ignores player and reaches station "+station);
            }
            player.Teleport(new Vector3(.65f,.1f,4));
            c.CloseModal();Call(c,"UpdateWalkCourse");FactoryLabTools.SavePreview(c,"v2-walk-1920",1920,1080);
            for(int step=0;step<6;step++)
            {
                c.Progress.accepted=true;
                if(step==0)c.views[0].GetComponent<FactoryInteraction>().Interact();
                if(step==1)for(int i=1;i<=4;i++)c.views[i].GetComponent<FactoryInteraction>().Interact();
                if(step==2){c.StartExperiment();Check(c.Running,"baseline starts in authorized stage");c.Simulation.Advance(10000);c.PauseExperiment();c.SaveCompletedRun();Call(c,"OpenWalkEvents",0);}
                if(step==3)
                {
                    Call(c,"OpenWalkEvents",0);
                    c.Progress.transportEvidence=JsonConvert.SerializeObject(c.Baseline.events.First(e=>e.subject=="LAMP-01"&&e.activity=="transport"));
                    c.Progress.waitingEvidence=JsonConvert.SerializeObject(c.Baseline.events.First(e=>e.subject=="LAMP-01"&&e.activity=="delay"));
                }
                if(step==4)
                {
                    Call(c,"PreviewWalkSwap",1,3);FactoryLabTools.SavePreview(c,"v2-swap-1920",1920,1080);
                    Call(c,"ConfirmWalkSwap",1,3);Check(c.Progress.swaps==1 && c.Progress.layoutVersion==1,"confirmed swap creates layout version");
                    Check(c.Baseline.elapsed==1771 && c.Simulation.Time==0,"swap preserves baseline and resets order");
                    c.Progress.Prediction.trends=new[]{0,2,0};
                }
                if(step==5){c.StartExperiment();Check(c.Running && c.Progress.Prediction.locked,"prediction locks before adjusted run");c.Simulation.Advance(10000);c.PauseExperiment();c.SaveCompletedRun();}
                Check(c.Progress.Gate()=="","stage "+step+" observation and operation gate");
                foreach(var q in FactoryQuestions.All.Where(q=>q.step==step))
                {
                    var r=c.Progress.Record(q.id);q.Bind(r,c.Progress,c.Simulation.Config.ProductDistance());q.Submit(r,r.expected.ToString(CultureInfo.InvariantCulture));
                }
                Check(c.Progress.Ready,"stage "+step+" ready after mastery");
                Call(c,"OpenWalkQuestion");
                if(step==2)foreach(var size in new[]{new[]{1366,768},new[]{1920,1080},new[]{2560,1440}})FactoryLabTools.SavePreview(c,"v2-question-"+size[0],size[0],size[1]);
                Call(c,"CompleteWalkStep");
            }
            Check(c.Progress.complete && c.Progress.FirstScore==100 && c.Progress.MasteryScore==100,"six-stage complete course");
            c.ExportReport();Check(File.Exists(c.LastReportPath),"v2 HTML and JSON report export");
            Check(!File.ReadAllText(c.LastReportPath).Contains("待教师复核"),"report has no subjective grading");
            File.WriteAllText(".work/factory-lab/v2-validation.json",JsonConvert.SerializeObject(new{status="PASS",checks,report=c.LastReportPath,liveVoice="NOT TESTED"},Formatting.Indented));
            Debug.Log("FACTORY_V2_VALIDATION_PASS "+checks.Count);
        }
        public static void InspectRay()
        {
            EditorSceneManager.OpenScene(FactoryLabTools.ScenePath);
            var c=UnityEngine.Object.FindObjectOfType<FactoryLabController>();c.firstPersonCourse=true;c.Initialize();
            var player=c.GetComponentInChildren<FactoryFirstPerson>();
            for(int i=0;i<6;i++)
            {
                var center=c.views[i].transform.position;float sign=center.z<4?1:-1;
                player.Teleport(center+new Vector3(0,.1f,sign*1.4f));Physics.SyncTransforms();
                var direction=(center+Vector3.up*.9f-player.view.transform.position).normalized;
                bool hitSomething=Physics.Raycast(player.view.transform.position,direction,out var hit,2f);
                Debug.Log("RAY "+i+" origin="+player.view.transform.position+" center="+center+" collider="+(hit.collider==null?"none":hit.collider.name)+" distance="+hit.distance+" own="+c.views[i].GetComponent<BoxCollider>().bounds+" enabled="+c.views[i].GetComponent<BoxCollider>().enabled);
            }
        }
        public static void ValidateAndBuild()
        {
            Validate();
            // Discard transient validation objects; build the checked-in scene with runtime initialization.
            EditorSceneManager.OpenScene(FactoryLabTools.ScenePath);
            FactoryDeliveryTools.BuildWindows();
        }
    }
}
