using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace FactoryLab
{
    [Serializable]
    public sealed class FactoryRun
    {
        public string id, layout, parameterKey;
        public int quantity;
        public double elapsed, loaded, empty, waiting, flow;
        public FloorPoint[] centers, ports;
        public int[] queues;
        public FactoryEventRecord[] events;
        public static FactoryRun Capture(FactorySimulation sim, string id)
        {
            if (!sim.Complete) throw new InvalidOperationException("订单尚未完成，不能保存为完成结果。");
            return new FactoryRun { id = id, layout = sim.Config.LayoutId, quantity = sim.Products.Length,
                parameterKey = JsonConvert.SerializeObject(new { quantity = sim.Products.Length, sim.Config.Speed, sim.Config.LoadSeconds, sim.Config.UnloadSeconds,
                    stations = sim.Config.Stations.Select(s => new { s.Id, s.Cycle, s.Capacity }) }),
                elapsed = sim.Time, loaded = sim.LoadedDistance, empty = sim.EmptyDistance, waiting = sim.Products.Sum(p => p.WaitingSeconds),
                flow = sim.Products.Average(p => p.FinishedAt.Value - p.ReleasedAt.Value), centers = sim.Config.Centers.ToArray(), ports = sim.Config.Ports.ToArray(),
                queues = sim.Stations.Select(s => s.MaxInputCount).ToArray(), events = sim.Events.Select(e => new FactoryEventRecord {
                    subject=e.subject, activity=e.activity, reason=e.reason, from=e.from, to=e.to, resource=e.resource,
                    startSeconds=e.startSeconds,endSeconds=e.endSeconds,distanceM=e.distanceM }).ToArray() };
        }
        public static bool Comparable(FactoryRun a, FactoryRun b) => a != null && b != null && a.parameterKey == b.parameterKey;
        public static string Compare(FactoryRun a, FactoryRun b)
        {
            if (!Comparable(a,b)) return "请先完成原始和改善订单。数量、工时、缓冲与搬运参数必须相同。";
            Func<double,double,string> metric = (x,y) => x.ToString("0.0") + " → " + y.ToString("0.0") + "（变化 " + (y-x).ToString("+0.0;-0.0;0") + "）";
            return "原始 → 改善（同参数、" + a.quantity + " 件）\n订单完成 / s    " + metric(a.elapsed,b.elapsed) +
                "\n载货距离 / m    " + metric(a.loaded,b.loaded) + "\n车辆空驶 / m    " + metric(a.empty,b.empty) +
                "\n累计等待 / s    " + metric(a.waiting,b.waiting) + "\n平均流动时间 / s    " + metric(a.flow,b.flow) +
                "\n最高输入队列（装配/紧固/检验/包装）\n" + string.Join(" / ",a.queues.Skip(1).Take(4)) + " → " + string.Join(" / ",b.queues.Skip(1).Take(4)) +
                "\n距离减少不代表等待或产量必然改善，请分别解释。";
        }
    }

    [Serializable]
    public sealed class FactoryWorkbook
    {
        public int version = 1, step;
        public bool[] submitted = new bool[6];
        public int[] choices = Enumerable.Repeat(-1, 15).ToArray();
        public string boundary = "", routeEvidence = "", waitEvidence = "", analysis = "", proposal = "", prediction = "", conclusion = "", maintain = "";
        public int ecrs = -1;
        public int researchObject = -1, researchBoundary = -1, constraints = -1;
        public string baselineId = "", improvedId = "";
        public int ObjectiveScore => Enumerable.Range(0,5).Sum(i => choices[i] == FactoryLesson.ChartAnswers[i] ? 3 : 0)
            + Enumerable.Range(0,10).Sum(i => choices[5+i] == FactoryLesson.FlowAnswers[i] ? 2 : 0)
            + (submitted[0] && researchObject == 0 && researchBoundary == 0 && constraints == 0 ? 5 : 0);
    }

    public static class FactoryLesson
    {
        public static readonly string[] Steps = { "选择 · 确定研究边界", "记录 · 工艺程序图", "记录 · 流程程序图", "分析 · 5W1H 问题定位", "建立 · ECRS 改善方案", "实施与维持 · 比较和反思" };
        public static readonly string[] Types = { "未选择", "加工 ○", "检查 □", "搬运 →", "等待 D", "储存 ▽" };
        // Choices use 0..4 (dropdown index minus one).
        public static readonly string[] ChartLabels = { "底座 + 灯杆 → 装配", "灯头模块汇入 → 定位", "紧固螺钉", "外观、紧固及亮灯检查", "包装" };
        public static readonly int[] ChartAnswers = {0,0,0,1,0};
        public static readonly string[] FlowLabels = { "领取前，配套物料受控保管", "供料区到装配台，含装卸", "装配台完成装配", "装配输出料位等待搬运车", "装配台到紧固台", "紧固作业", "检验台进行检查 45 秒", "输入队列等待检验台空闲", "包装作业", "成品入库等待后续出库" };
        public static readonly int[] FlowAnswers = {4,2,0,3,2,0,1,3,0,4};
        public static string Hint(int step, int level)
        {
            string[][] hints = {
                new[]{"观察一件台灯从哪里领取、到哪里结束。","研究对象保持单一；列出工艺、人员与质量约束。","对象是一件产品及配套物料，起点领取，终点成品入库。检验、工序和人数均不能随意改变。"},
                new[]{"观察装配、紧固、检验和包装。","工艺程序图只记录加工与检查，以及组件汇入。","底座/灯杆和灯头汇入装配；前三项加工，第四项检查，第五项加工。两项装配合并使用 30 秒，不重复累计。"},
                new[]{"查看选中产品的活动与事件。","加工、检查、搬运、等待、储存分别判断；保管目的比停留长短重要。","检验中的 45 秒是检查；检验前等资源是等待；库存受控保管是储存。空车返回不属于产品活动。"},
                new[]{"先完成原始 20 件订单，寻找长距离运输和等待事件。","用 5W1H 提问：何事、为何、何处、何时、何人、如何。","选一条搬运和一条等待作为证据，写出事件编号、产品和原因，不能只说布局不好。"},
                new[]{"切换俯视，注意相邻工序的交接点。","用 ECRS 检查取消、合并、重排、简化，并遵守约束。","当前合法操作是交换作业工位，属于重排；预测距离与等待分别如何变化，然后实际重跑。"},
                new[]{"比较同参数的两个完整订单。","分别检查载货、空驶、等待、流动时间和订单时间。","说明有效与无效之处；保留检验并提出下一轮测量。教学仿真不能直接证明真实工厂结果。"}
            };
            return hints[Math.Max(0,Math.Min(5,step))][Math.Max(0,Math.Min(2,level))];
        }
        public static string ValidateStep(FactoryWorkbook book, int step, FactoryRun baseline, FactoryRun improved)
        {
            if (step > 0 && !book.submitted[step-1]) return "请先提交前一个任务。";
            switch(step)
            {
                case 0: return book.researchObject != 0 || book.researchBoundary != 0 || book.constraints != 0 ? "请选择单件产品、领取到入库的边界，以及保留质量和资源约束。" : book.boundary.Length < 12 ? "请写明研究对象、领取起点、入库终点和不可取消检验等约束。" : "";
                case 1: return book.choices.Take(5).Any(x=>x<0) ? "请完成五个工艺节点分类。" : "";
                case 2: return book.choices.Skip(5).Any(x=>x<0) ? "请完成十项流程活动分类。" : "";
                case 3: return baseline == null || baseline.quantity != 20 ? "请先运行并完成原始 20 件订单。" :
                    string.IsNullOrWhiteSpace(book.routeEvidence) || string.IsNullOrWhiteSpace(book.waitEvidence) || book.analysis.Length<20 ? "请选择搬运和等待证据，并填写 5W1H 分析（至少 20 字）。" : "";
                case 4: return book.ecrs < 0 || book.proposal.Length<12 || book.prediction.Length<12 ? "请选择 ECRS，填写改善理由和运行前预测（各至少 12 字）。" : "";
                case 5: return !FactoryRun.Comparable(baseline,improved) ? "需要两个同参数的完整订单结果。" : book.conclusion.Length<20 || book.maintain.Length<12 ? "请结合实际数据填写结论（至少 20 字）和维持方案（至少 12 字）。" : "";
                default: return "未知任务。";
            }
        }
        public static string Report(FactoryWorkbook book, FactoryRun baseline, FactoryRun improved)
        {
            Func<string,string> h = WebUtility.HtmlEncode;
            var s = new StringBuilder("<!doctype html><html lang='zh-CN'><meta charset='utf-8'><title>程序分析实验记录</title><style>body{font:16px/1.8 'Microsoft YaHei',sans-serif;color:#142434;max-width:1100px;margin:40px auto;padding:24px}h1,h2{color:#076c69}table{border-collapse:collapse;width:100%}td,th{padding:8px;border:1px solid #ccd8de;text-align:left}pre{white-space:pre-wrap;background:#eef5f5;padding:16px}small{color:#536879}@media print{body{margin:0}tr{break-inside:avoid}}</style><h1>台灯装配 · 程序分析实验记录</h1>");
            s.Append("<p>教学设定数据；单件研究对象；生成时间 " + h(DateTime.Now.ToString("yyyy-MM-dd HH:mm")) + "</p>");
            s.Append("<h2>评分与任务状态</h2><p>客观分 " + book.ObjectiveScore + " / 40。问题定位 20、改善方案 20、效果解释 20 分由教师复核，未自动计分。非最终总分。</p><p>已提交任务："+book.submitted.Count(x=>x)+" / 6</p>");
            s.Append("<p>ECRS 分类："+new[]{"未选择","取消 E","合并 C","重排 R","简化 S"}[Math.Max(0,Math.Min(4,book.ecrs+1))]+"（待教师复核）</p>");
            var labels = new[]{"研究边界","搬运证据","等待证据","5W1H 分析","改善方案","运行前预测","效果解释","维持与后续验证"};
            var values = new[]{book.boundary,book.routeEvidence,book.waitEvidence,book.analysis,book.proposal,book.prediction,book.conclusion,book.maintain};
            for(int i=0;i<labels.Length;i++) s.Append("<h2>"+labels[i]+"</h2><pre>"+h(values[i])+"</pre>");
            s.Append("<h2>工艺程序图</h2><p>底座 + 灯杆 → 装配 ← 灯头模块 → 紧固 → 检验 → 包装。装配的两个教学子节点合计 30 秒。</p><table><tr><th>节点</th><th>学生分类</th><th>参考分类</th></tr>");
            for(int i=0;i<5;i++) s.Append("<tr><td>"+ChartLabels[i]+"</td><td>"+Types[book.choices[i]+1]+"</td><td>"+Types[ChartAnswers[i]+1]+"</td></tr>");
            s.Append("</table><h2>流程活动分类</h2><table><tr><th>活动</th><th>学生分类</th><th>参考分类</th></tr>");
            for(int i=0;i<10;i++) s.Append("<tr><td>"+FlowLabels[i]+"</td><td>"+Types[book.choices[5+i]+1]+"</td><td>"+Types[FlowAnswers[i]+1]+"</td></tr>");
            s.Append("</table><h2>同参数结果比较</h2><pre>"+h(FactoryRun.Compare(baseline,improved))+"</pre>");
            foreach(var run in new[]{baseline,improved}.Where(r=>r!=null))
            {
                s.Append("<h2>"+h(run.layout)+" · LAMP-01 流程账本</h2><small>实验 "+h(run.id)+"；从初始库存到入库；空车事件不混入产品记录。</small><table><tr><th>活动</th><th>起点 → 终点</th><th>起止 / s</th><th>距离 / m</th><th>原因</th></tr>");
                foreach(var e in run.events.Where(e=>e.subject=="LAMP-01")) s.Append("<tr><td>"+h(FactoryLabController.ActivityName(e.activity))+"</td><td>"+h(e.from+" → "+e.to)+"</td><td>"+e.startSeconds.ToString("0.0",CultureInfo.InvariantCulture)+" → "+(e.endSeconds?.ToString("0.0",CultureInfo.InvariantCulture)??"未出库")+"</td><td>"+e.distanceM+"</td><td>"+h(e.reason)+"</td></tr>");
                s.Append("</table>");
            }
            return s.Append("<p>教材来源：基础工业工程第四章，程序分析 p.41–45、工艺程序 p.45–54、流程程序 p.55–72、布置与经路 p.73–88。主观题待教师复核。</p></html>").ToString();
        }
    }
}
