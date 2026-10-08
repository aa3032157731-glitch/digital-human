using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace FactoryLab
{
    [Serializable] public sealed class FactoryAttempt
    {
        public string answer, utc;
        public bool correct, hintUsed;
    }
    [Serializable] public sealed class FactoryAnswer
    {
        public string questionId, evidence, sourceId;
        public double expected;
        public bool bound;
        public int hints;
        public List<FactoryAttempt> attempts = new List<FactoryAttempt>();
        public int FirstScore => attempts.Count > 0 && attempts[0].correct ? 5 : 0;
        public int MasteryScore => attempts.Any(a => a.correct) ? 5 : 0;
    }
    [Serializable] public sealed class FactoryPrediction
    {
        public int layoutVersion;
        public int[] trends = { -1, -1, -1 };
        public bool locked;
        public string runId;
    }
    [Serializable] public sealed class FactoryProgressV2
    {
        public int version = 2, step, layoutVersion, swaps;
        public bool accepted, complete, ledgerViewed;
        public bool[] visited = new bool[6];
        public bool[] completed = new bool[6];
        public string layoutJson, transportEvidence, waitingEvidence;
        public FactoryRun baseline, improved;
        public List<FactoryAnswer> answers = new List<FactoryAnswer>();
        public List<FactoryPrediction> predictions = new List<FactoryPrediction>();
        public FactoryPrediction Prediction => predictions.LastOrDefault(p => p.layoutVersion == layoutVersion);
        public FactoryAnswer Record(string id)
        {
            var answer = answers.FirstOrDefault(a => a.questionId == id);
            if (answer == null) { answer = new FactoryAnswer { questionId = id }; answers.Add(answer); }
            return answer;
        }
        public int FirstScore => answers.Sum(a => a.FirstScore);
        public int MasteryScore => answers.Sum(a => a.MasteryScore);
        public string Gate()
        {
            if (!accepted) return "先到老师处接受当前任务。";
            switch (step)
            {
                case 0: return visited[0] ? "" : "到供料区按 E 观察研究对象。";
                case 1: return Enumerable.Range(1,4).All(i=>visited[i]) ? "" : "分别到装配、紧固、检验、包装工位按 E 观察。";
                case 2: return baseline != null && baseline.quantity == 20 && ledgerViewed ? "" : "在控制台运行并完成原始 20 件订单，查看 LAMP-01 记录。";
                case 3: return string.IsNullOrEmpty(transportEvidence) || string.IsNullOrEmpty(waitingEvidence) ? "选择一条实际搬运事件和一条等待事件。" : "";
                case 4: return swaps == 0 ? "在现场选择两个作业工位并确认交换。" : Prediction == null || Prediction.trends.Any(t=>t<0) ? "提交距离、等待、完成时间的趋势预测。" : "";
                case 5: return FactoryRun.Comparable(baseline, improved) ? "" : "在控制台完成同参数的调整后 20 件订单。";
                default: return "未知任务。";
            }
        }
        public bool Ready => Gate().Length == 0 && FactoryQuestions.All.Where(q=>q.step==step).All(q=>Record(q.id).MasteryScore==5);
    }
    public sealed class FactoryQuestion
    {
        public string id, text, explanation, unit;
        public int step, correct;
        public string[] options;
        public bool Numeric => options == null;
        public FactoryQuestion(string id, int step, string text, string explanation, int correct, params string[] options)
        { this.id=id; this.step=step; this.text=text; this.explanation=explanation; this.correct=correct; this.options=options; }
        public void Bind(FactoryAnswer record, FactoryProgressV2 progress, double route)
        {
            if (record.bound) return;
            double value = correct;
            string evidence = explanation, source = "课程约束 v2";
            if (id == "q11")
            {
                var e = progress.baseline?.events.FirstOrDefault(x=>x.subject=="LAMP-01" && x.activity=="inspection" && x.endSeconds.HasValue);
                if (e == null) throw new InvalidOperationException("缺少已完成的 LAMP-01 检验事件。");
                value=e.endSeconds.Value-e.startSeconds; evidence=$"LAMP-01 检查：{e.startSeconds:0.0} → {e.endSeconds:0.0} s"; source=progress.baseline.id;
            }
            if (id == "q14") { if(progress.baseline==null)throw new InvalidOperationException("请先完成原始订单。"); value=progress.baseline.loaded; evidence=$"原始订单累计产品载货 {value:0.0} m"; source=progress.baseline.id; }
            if (id == "q17") { value=route; evidence=$"布局版本 {progress.layoutVersion}：供料→装配→紧固→检验→包装→成品，完整载货路线 {route:0.0} m"; source="layout-"+progress.layoutVersion; }
            if (id == "q20") { if(!FactoryRun.Comparable(progress.baseline,progress.improved))throw new InvalidOperationException("缺少同参数结果。"); value=progress.baseline.elapsed-progress.improved.elapsed; evidence=$"原始 {progress.baseline.elapsed:0.0} − 调整 {progress.improved.elapsed:0.0} s"; source=progress.baseline.id+"/"+progress.improved.id; }
            record.expected=value; record.evidence=evidence; record.sourceId=source; record.bound=true;
        }
        public bool Submit(FactoryAnswer record, string input)
        {
            if (!record.bound) throw new InvalidOperationException("题目尚未绑定证据。");
            if (record.MasteryScore==5) return true;
            bool parsed=double.TryParse(input,NumberStyles.Float,CultureInfo.InvariantCulture,out var value);
            bool valid=parsed && !double.IsNaN(value) && !double.IsInfinity(value);
            bool correctAnswer=valid && Math.Abs(value-record.expected)<=(Numeric ? .100000001 : 0);
            record.attempts.Add(new FactoryAttempt {answer=input,correct=correctAnswer,hintUsed=record.hints>0,utc=DateTime.UtcNow.ToString("O")});
            return correctAnswer;
        }
    }
    public static class FactoryQuestions
    {
        private static FactoryQuestion Q(int id,int step,string text,string explanation,int answer,params string[] options) => new FactoryQuestion("q"+id,step,text,explanation,answer,options);
        private static FactoryQuestion T(int id,int step,string text,string explanation,bool yes) => Q(id,step,text,explanation,yes?0:1,"正确","错误");
        private static FactoryQuestion N(int id,int step,string text,string unit,string explanation) => new FactoryQuestion("q"+id,step,text,explanation,0,null){unit=unit};
        public static readonly string[] Steps = { "认识研究对象", "认识工艺程序", "记录产品流程", "用证据定位问题", "建立改善方案", "验证与总结" };
        public static readonly string[] Instructions = {
            "本次研究单件台灯及配套物料。请到供料区观察，从领取物料到成品入库；保持工艺、工时、人数与检验要求。",
            "请观察装配、紧固、检验与包装四个工位。底座和灯杆、灯头模块汇入装配，区分加工与检查。",
            "请到控制台启动原始 20 件订单，跟踪 LAMP-01。查看加工、检查、搬运、等待、储存五类实际事件。",
            "请查看已完成的原始订单，选择一条搬运和一条等待事件。用何事、为何、何处、何时、何人、如何分析事实。",
            "请靠近两个作业工位，按 E 依次选择并预览交换。确认后提交距离、等待、完成时间的趋势预测。",
            "请在相同参数下运行调整后订单，对照载货距离、等待和完成时间。结果变差也可完成学习，结论必须依据实际证据。"
        };
        public static readonly FactoryQuestion[] All = {
            Q(1,0,"本实验的研究对象是什么？","流程程序分析必须保持单一研究对象。",0,"单件台灯及配套物料","所有车辆活动","整个企业的销售"),
            Q(2,0,"研究的起点和终点是什么？","边界从领取配套物料到成品入库。",1,"采购到销售","领取物料到成品入库","开灯到关灯"),
            T(3,0,"为了缩短时间，可以取消检验。","本实验必须保留检验、人数、工序及工时。",false),
            Q(4,1,"底座、灯杆与灯头模块在哪里汇入？","组件在装配环节汇入，两项装配子节点合计 30 秒。",0,"装配","检验","成品库"),
            Q(5,1,"紧固螺钉属于哪类活动？","紧固改变装配状态，属于加工 ○。",0,"加工 ○","检查 □","储存 ▽"),
            Q(6,1,"确认外观、紧固及亮灯状态属于什么？","确认质量和特性属于检查 □。",1,"搬运 →","检查 □","加工 ○"),
            T(7,1,"工艺程序图只记录加工、检查及组件汇入关系。","搬运、等待、储存在流程程序图中进一步记录。",true),
            Q(8,2,"产品在输入队列等待检验台空闲，属于什么？","等待资源属于等待 D，不按停留时长判断储存。",1,"储存 ▽","等待 D","检查 □"),
            Q(9,2,"领取前受控保管的配套物料属于什么？","按规则受控保管属于储存 ▽。",2,"加工 ○","等待 D","储存 ▽"),
            T(10,2,"空车返回应算进 LAMP-01 的产品搬运距离。","空驶是车辆资源活动，不是产品载货搬运。",false),
            N(11,2,"LAMP-01 检验事件实际持续多长时间？","s","用该检验事件结束时间减去开始时间。"),
            Q(12,3,"哪种提问符合基于证据的 5W1H 分析？","先定位具体事件及资源原因，再分析何处与如何改善。",0,"该事件为何在这里等待搬运车？","布局不好就全部重做","检验最长所以必然是瓶颈"),
            T(13,3,"仅凭一次等待事件就能证明整个系统的瓶颈。","还需要队列、资源和完整订单等证据交叉分析。",false),
            N(14,3,"原始 20 件订单累计产品载货距离是多少？","m","读取原始完成快照的载货距离，不包含空驶。"),
            Q(15,4,"交换作业工位属于哪类 ECRS 改善？","仅改变空间布置属于重排 R。",2,"取消 E","合并 C","重排 R","简化 S"),
            T(16,4,"交换位置后，工艺顺序、人数、工时和检验要求保持不变。","本实验只改变布局，保留比较所需的其他参数。",true),
            N(17,4,"当前布局单件完整载货路线距离是多少？","m","沿全部工序交接点及中央通道累计，不取两点直线距离。"),
            Q(18,5,"哪种总结符合实际实验结论的要求？","各指标应分别判断，变差也是有效实验结果。",1,"距离减少则所有指标一定改善","分别比较距离、等待、完成时间，并限定教学条件","只能接受最优布局"),
            T(19,5,"调整后订单更慢，也能依据真实数据完成本课程。","学习评价依据证据判断，不要求找到唯一最优布局。",true),
            N(20,5,"原始订单时间减去调整后订单时间是多少？","s","原始减调整；负数表示调整后更慢。")
        };
        public static string Report(FactoryProgressV2 p)
        {
            Func<string,string> h=WebUtility.HtmlEncode;
            var s=new StringBuilder("<!doctype html><html lang='zh-CN'><meta charset='utf-8'><title>第一人称实训记录 v2</title><style>body{max-width:1100px;margin:40px auto;font:16px/1.8 sans-serif}td,th{border:1px solid #bcc;padding:8px}table{border-collapse:collapse;width:100%}pre{white-space:pre-wrap}</style><h1>台灯装配实训 · v2</h1>");
            s.Append($"<p>首次分 {p.FirstScore}/100；掌握分 {p.MasteryScore}/100；完成环节 {p.completed.Count(x=>x)}/6</p><h2>现场任务</h2><ul>");
            for(int i=0;i<6;i++)s.Append($"<li>{h(Steps[i])}：{(p.completed[i]?"已完成":"未完成")}；工位 {i+1} 观察：{p.visited[i]}</li>");
            s.Append($"</ul><p>交换次数 {p.swaps}，布局版本 {p.layoutVersion}</p><pre>{h(p.transportEvidence)}\n{h(p.waitingEvidence)}</pre><h2>答题记录</h2><table><tr><th>题目</th><th>首次 / 掌握</th><th>尝试 / 提示</th><th>证据与提交</th></tr>");
            foreach(var q in All) { var r=p.answers.FirstOrDefault(a=>a.questionId==q.id); s.Append($"<tr><td>{h(q.text)}</td><td>{r?.FirstScore??0} / {r?.MasteryScore??0}</td><td>{r?.attempts.Count??0} / {r?.hints??0}</td><td>{h(r?.sourceId)}<br>{h(r?.evidence)}<br>{h(r==null?"":string.Join("; ",r.attempts.Select(a=>$"{a.utc}: {a.answer} ({a.correct}, 提示 {a.hintUsed})")))}</td></tr>"); }
            s.Append("</table><h2>布局预测</h2>");
            foreach(var prediction in p.predictions)s.Append($"<p>版本 {prediction.layoutVersion}；距离 / 等待 / 时间：{h(string.Join(" / ",prediction.trends.Select(t=>t<0?"未选择":new[]{"减少","不变","增加"}[t])))}；锁定 {prediction.locked}；运行 {h(prediction.runId)}</p>");
            s.Append("<h2>结果比较</h2><pre>"+h(FactoryRun.Compare(p.baseline,p.improved))+"</pre>");
            foreach(var run in new[]{p.baseline,p.improved}.Where(r=>r!=null)) { s.Append("<h2>"+h(run.id)+" · LAMP-01 流程程序图</h2><pre>"); foreach(var e in run.events.Where(e=>e.subject=="LAMP-01"))s.Append(h($"{FactoryLabController.ActivityName(e.activity)} | {e.startSeconds:0.0} → {e.endSeconds:0.0} s | {e.distanceM:0.0} m | {e.reason}\n")); s.Append("</pre>"); }
            return s.Append("<p>教学仿真数据，需现场校准后才能应用于真实工厂。</p></html>").ToString();
        }
    }
}
