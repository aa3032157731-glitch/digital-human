using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace FactoryLab
{
    // Explicit opt-in: exercises the actual runtime components, UI handlers and PhysX.
    // This is scripted acceptance, not a claim of manual keyboard/microphone testing.
    public sealed class FactoryV2PlayerValidation : MonoBehaviour
    {
        private readonly List<string> checks=new List<string>();
        private FactoryLabController c;
        private string folder;
        private bool RestoreTest => Environment.GetCommandLineArgs().Contains("-factory-v2-restoretest");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var args=Environment.GetCommandLineArgs();
            bool restore=args.Contains("-factory-v2-restoretest");
            if(!args.Contains("-factory-v2-selftest") && !restore)return;
            Application.runInBackground=true;
            FactoryLabController.StorageOverride=Path.Combine(Application.persistentDataPath,"FactoryLab","V2Acceptance",DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            if(restore){int index=Array.IndexOf(args,"-factory-v2-save-root");if(index<0 || index+1>=args.Length)throw new ArgumentException("Missing save root");FactoryLabController.StorageOverride=args[index+1];}
            DontDestroyOnLoad(new GameObject("Factory v2 runtime acceptance",typeof(FactoryV2PlayerValidation)));
        }
        private void Check(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);}
        private void Call(string method,params object[] args)=>typeof(FactoryLabController).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c,args);
        private void Click(string name)
        {
            var button=c.Hud.GetComponentsInChildren<Button>().Single(b=>b.name==name && b.gameObject.activeInHierarchy);
            Check(button.interactable,"button enabled: "+name);button.onClick.Invoke();
        }
        private IEnumerator Start()
        {
            folder=FactoryLabController.StorageOverride;Directory.CreateDirectory(folder);
            var run=Exercise();
            while(true)
            {
                bool next;object current=null;
                try{next=run.MoveNext();if(next)current=run.Current;}
                catch(Exception e){File.WriteAllText(Path.Combine(folder,RestoreTest?"restore-result.json":"runtime-result.json"),JsonConvert.SerializeObject(new{status="FAIL",checks,error=e.ToString()},Formatting.Indented));Debug.LogException(e);Application.Quit(1);yield break;}
                if(!next)break;yield return current;
            }
            File.WriteAllText(Path.Combine(folder,RestoreTest?"restore-result.json":"runtime-result.json"),JsonConvert.SerializeObject(new{status="PASS",checks,manualKeyboard="NOT VERIFIED",liveAsrTts="NOT VERIFIED"},Formatting.Indented));
            Debug.Log("FACTORY_V2_RUNTIME_PASS "+folder);Application.Quit(0);
        }
        private IEnumerator Exercise()
        {
            for(int i=0;i<20;i++)yield return null;
            c=FindObjectOfType<FactoryLabController>();Check(c!=null && c.Simulation!=null,"runtime initialized");
            c.teacherVoice.enabled=false;
            var player=c.GetComponentInChildren<FactoryFirstPerson>();var body=player.GetComponent<CharacterController>();
            if(RestoreTest)
            {
                Check(c.Progress.complete && c.Progress.FirstScore==100 && c.Progress.MasteryScore==100,"completed lesson and scores restored on startup");
                Check(c.Baseline.elapsed==1771 && c.Improved!=null && c.Progress.layoutVersion==1,"both immutable result snapshots and swapped layout restored");
                Check(c.Simulation.Time==0 && !c.Running,"restored session resets unfinished live order");
                Check(Vector3.Distance(player.transform.position,new Vector3(.65f,.1f,4))<.1f,"restored player starts at safe checkpoint");
                Check(c.Progress.Prediction.locked && c.Progress.Prediction.runId==c.Improved.id,"prediction stays bound to completed snapshot");
                yield break;
            }
            Check(!player.Walking && Cursor.lockState==CursorLockMode.None,"initial menu releases cursor");
            Call("ResumeWalk");Check(player.Walking,"explicit resume enables movement");player.SetWalking(false);
            player.Teleport(new Vector3(.4f,.1f,4));Physics.SyncTransforms();
            for(int i=0;i<40;i++)body.Move(Vector3.left*.05f);
            Check(player.transform.position.x>.15f,"left wall blocks character");
            player.Teleport(new Vector3(11.6f,.1f,7.5f));Physics.SyncTransforms();
            for(int i=0;i<40;i++)body.Move(Vector3.right*.05f);
            Check(player.transform.position.x<11.9f,"right boundary blocks character");
            for(int i=0;i<6;i++)
            {
                var center=c.views[i].transform.position;float sign=center.z<4?1:-1;
                player.Teleport(center+new Vector3(0,.1f,sign*1.4f));Physics.SyncTransforms();
                Vector3 direction=(center+Vector3.up*.9f-player.view.transform.position).normalized;
                Check(Physics.Raycast(player.view.transform.position,direction,out var hit,2f) && hit.collider.GetComponentInParent<FactoryInteraction>()==c.views[i].GetComponent<FactoryInteraction>(),"station "+i+" reachable by unobstructed 2m ray; origin="+player.view.transform.position+" center="+center+" hit="+(hit.collider==null?"none":hit.collider.name)+" distance="+hit.distance);
                for(int j=0;j<30;j++)body.Move(new Vector3(0,0,-sign*.05f));
                Check(Mathf.Abs(player.transform.position.z-center.z)>.65f,"station "+i+" blocks character");
            }
            player.Teleport(new Vector3(.65f,.1f,4));
            player.SendMessage("OnApplicationFocus",false);Check(!player.Walking && !c.Running,"focus loss pauses and releases input");
            c.OpenTeacher();var input=c.Hud.GetComponentsInChildren<InputField>().First();input.Select();
            Check(!player.Walking,"text composer blocks movement");c.CloseModal();
            for(int step=0;step<6;step++)
            {
                Call("OpenWalkTeacher");Click("我准备好了");
                if(step==0)c.views[0].GetComponent<FactoryInteraction>().Interact();
                if(step==1)for(int station=1;station<5;station++)c.views[station].GetComponent<FactoryInteraction>().Interact();
                if(step==2 || step==5)
                {
                    Call("OpenWalkConsole");Click("开始 / 继续订单");Check(c.Running,"order running stage "+step);
                    Call("OpenWalkPause");Check(!c.Running && !player.Walking,"modal pauses order and player stage "+step);
                    Call("ResumeWalk"); // time is still zero, so explicitly start via console
                    Call("OpenWalkConsole");Click("开始 / 继续订单");
                    c.Simulation.Advance(10000);c.SaveCompletedRun();c.PauseExperiment();Call("OpenWalkEvents",0);
                }
                if(step==3)
                {
                    for(int page=0;page<20 && c.Progress.Gate().Length>0;page++)
                    {
                        Call("OpenWalkEvents",page);
                        foreach(var button in c.Hud.GetComponentsInChildren<Button>().Where(b=>b.name=="选择证据").ToArray())button.onClick.Invoke();
                    }
                    Check(!string.IsNullOrEmpty(c.Progress.transportEvidence) && !string.IsNullOrEmpty(c.Progress.waitingEvidence),"real transport and waiting rows selected");
                }
                if(step==4)
                {
                    Call("OpenWalkTask");Click("现场选择两个工位");
                    c.views[0].GetComponent<FactoryInteraction>().Interact();Check(c.Progress.swaps==0,"fixed area rejected");
                    c.views[1].GetComponent<FactoryInteraction>().Interact();c.views[3].GetComponent<FactoryInteraction>().Interact();
                    Click("确认重置并交换");
                    Check(c.Progress.swaps==1 && c.Baseline.elapsed==1771,"swap applied with baseline preserved");
                    var controls=c.Hud.GetComponentsInChildren<Dropdown>();
                    Check(controls.Length==3,"three structured prediction controls");foreach(var dropdown in controls)dropdown.value=1;
                    Check(c.Progress.Gate()=="","prediction allows quiz");
                }
                Call("OpenWalkTask");Click("开始 / 继续逐题作答");
                foreach(var question in FactoryQuestions.All.Where(q=>q.step==step))
                {
                    var record=c.Progress.Record(question.id);
                    if(question.Numeric){var number=c.Hud.GetComponentsInChildren<InputField>().Single();number.text=record.expected.ToString(CultureInfo.InvariantCulture);Click("提交数字");}
                    else Click(question.options[question.correct]);
                    Check(record.MasteryScore==5,"UI submission scored "+question.id);Click("下一题 / 返回任务");
                }
                Check(c.Progress.Ready,"stage "+step+" complete runtime gate");Call("OpenWalkTeacher");Click(step==5?"完成课程":"确认前往下一站");
                yield return new WaitForSecondsRealtime(.8f);
            }
            Check(c.Progress.complete && c.Progress.FirstScore==100 && c.Progress.MasteryScore==100,"full runtime lesson completed");
            Check(c.Progress.Prediction.locked && c.Progress.Prediction.runId==c.Improved.id,"prediction bound to actual completed run");
            c.ExportReport();Check(File.Exists(c.LastReportPath),"runtime report saved");
            var restored=JsonConvert.DeserializeObject<FactoryProgressV2>(File.ReadAllText(Path.Combine(folder,"Course-v2","progress.json")));
            Check(restored.complete && restored.MasteryScore==100 && restored.baseline.id==c.Baseline.id,"atomic save restores scores and evidence");
            foreach(var size in new[]{new[]{1366,768},new[]{1920,1080},new[]{2560,1440}})
            {
                Screen.SetResolution(size[0],size[1],FullScreenMode.Windowed);
                for(int i=0;i<10;i++)yield return null;
                Call("OpenWalkTeacher");
                using(var capture=new FactoryOffscreenCapture(c,size[0],size[1]))capture.Save(Path.Combine(folder,"runtime-summary-"+size[0]+".png"));
                Check(c.Hud.GetComponentsInChildren<Text>().All(t=>t.rectTransform.rect.width>=0),"UI rendered at "+size[0]);
            }
            c.CloseModal();player.Teleport(new Vector3(.65f,.1f,4));
            using(var capture=new FactoryOffscreenCapture(c,Screen.width,Screen.height))capture.Save(Path.Combine(folder,"runtime-world.png"));
        }
    }
}
