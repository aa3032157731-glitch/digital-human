# Rag：数字人知识库检索服务

数字人每轮回答前，会把用户问题发给这个本机服务。服务用 BGE-M3 把问题编码成向量，在 Qdrant 里找出最相关的资料片段返回给 Unity；Unity 把片段放进这一轮的 system 提示，再交给大模型生成回答。

```text
准备资料（资料变了才运行）  data/documents → loader → splitter → BGE-M3 → Qdrant 集合 rag_tutorial
每轮提问                    Unity → POST /retrieve → BGE-M3 编码问题 → Qdrant 检索 → 片段 → 大模型回答
```

本服务只负责找资料，不生成回答，也不保存聊天记录。

## 目录

| 路径 | 内容 |
| --- | --- |
| `backend/app/` | 读取资料、分段、向量编码、Qdrant 存取、检索 |
| `backend/ingest.py` | 把资料全量导入 Qdrant（每次重建集合） |
| `backend/api.py` | HTTP 服务：`GET /health`、`POST /retrieve` |
| `backend/cli.py` | 命令行检索，调试用 |
| `backend/scripts/evaluate.py` | 检索评估：Hit@1、Hit@3、阈值过滤效果 |
| `backend/tests/` | 单元测试，用内存版 Qdrant 和假模型，不需要 Docker 和 BGE-M3 |
| `infra/compose.yaml` | Qdrant 容器 |
| `data/documents/` | 知识库资料，支持 `.txt`、`.md`、`.json`（JSON 不支持嵌套） |
| `data/evaluations/questions.jsonl` | 评估题库 |

## 第一次准备

需要 Python 3.11 和 Docker Desktop。下面的命令都在本目录（`Rag/`）执行。

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r backend/requirements.txt
Copy-Item backend/.env.example backend/.env
Copy-Item infra/.env.example infra/.env
```

第一次导入或启动服务时，会把 BGE-M3（约 2.3 GB）下载到 `MODEL_CACHE`（默认 `models/huggingface`）。已经下载过的话，把 `backend/.env` 里的 `MODEL_CACHE` 改成那个目录的绝对路径即可复用。

## 日常启动

1. 打开 Docker Desktop，等它就绪。
2. 在本目录执行：

   ```powershell
   docker compose --env-file infra/.env -f infra/compose.yaml up -d
   .\.venv\Scripts\python.exe -m backend.ingest      # 只有资料或分段参数变了才需要
   .\.venv\Scripts\python.exe -m uvicorn backend.api:app --host 127.0.0.1 --port 8000
   ```

3. 看到 `Application startup complete` 后，在 Unity 里点 Play。

浏览器打开 <http://127.0.0.1:8000/docs> 可以直接试 `/retrieve`。

## 在数字人里启用

设置 → 机体模块 → 知识库 → 勾选“按问题检索知识库（RAG）” → 保存并应用 → 点“新对话”（旧会话里可能已经存了完整注入的知识库）。

- 每轮用“上一句用户消息 + 本句”检索，片段只放进本轮的 system 提示，不写入聊天记录；新会话也不再注入完整知识库。
- 对话面板的“知识库”标签显示“RAG：本轮检索到 N 段，最高相关度 0.xx”，Unity Console 打印 `[RAG] 检索词：…` 和拼好的资料。
- 检索服务没开、出错或 5 秒没响应时，数字人照常聊天，遇到需要资料的问题会说明知识库服务没有启动。
- 取消勾选后，行为和原来的完整注入模式一样。

同一页面可以改检索服务地址（默认 `http://127.0.0.1:8000`）和每轮片段数（默认 3，范围 1–10）。

## 接口

`POST /retrieve`

```json
{"question": "图书馆几点关门", "top_k": 3}
```

`top_k` 可省略（范围 1–10，省略时用 `TOP_K`）。响应：

```json
{"question": "图书馆几点关门", "hits": [{"chunk_id": "…", "source": "campus/library.md", "title": "星河校园图书馆（合成测试资料）", "section": "开放时间", "record_key": "md:0:开放时间", "text": "…", "score": 0.655}]}
```

- 没有匹配：HTTP 200，`hits` 为空。
- 问题为空、全是空格或 `top_k` 越界：422。
- Qdrant 不可用：503。

## 配置（`backend/.env`）

| 变量 | 作用 | 改完需要 |
| --- | --- | --- |
| `DOCUMENTS_DIR` | 资料目录，可写绝对路径（用正斜杠） | 重新 ingest |
| `CHUNK_SIZE` / `CHUNK_OVERLAP` / `MAX_TOKENS` | 分段长度、重叠、单段 token 上限 | 重新 ingest |
| `TOP_K` | 请求不带 `top_k` 时返回的片段数 | 重启 uvicorn |
| `MIN_SCORE` | 相关度阈值，低于它的片段不返回；留空表示不过滤 | 重启 uvicorn |
| `MAX_CONTEXT_CHARS` | 一次返回的片段总字数上限（按整段截断） | 重启 uvicorn |
| `BATCH_SIZE` | 编码批大小，只影响速度 | — |

想让 RAG 和数字人原来的完整注入读同一份资料，可以把 `DOCUMENTS_DIR` 指向数字人的知识库目录 `C:/Users/<用户名>/AppData/LocalLow/Camelminger/customDigitalman/VoiceChat/KnowledgeBase`。注意数字人原来的 `KnowledgeBaseStore` 只读 `.txt` 和 `.json`。

## 测试与评估

```powershell
.\.venv\Scripts\python.exe -m pytest backend/tests -q
.\.venv\Scripts\python.exe -m backend.scripts.evaluate                       # 默认题库
.\.venv\Scripts\python.exe -m backend.scripts.evaluate data/evaluations/其他题库.jsonl
```

题库每行一题：`{"question": "…", "answer": "资料原文里的一小段"}`。检索到的某段 `text` 包含 `answer` 才算找到；`answer` 为 `null` 表示资料里没有答案，理想结果是被 `MIN_SCORE` 挡掉。现有题库对应 `data/documents/campus` 下的合成资料，换成真实资料后要重写题目，并用口语提问，不要照抄原文。

评估取每题的前 5 条原始结果，不受 `MIN_SCORE` 和字数上限影响，阈值只在判分时使用：

- `hit@1` / `hit@3`：有答案的题里，含答案的片段排在第 1 / 前 3 的比例。
- `kept_by_threshold`：含答案的片段同时达到了阈值的比例。
- `rejected_unanswerable`：没有答案的题被阈值挡住的比例。

前三项只用有答案的题计算，最后一项只用没答案的题计算。

## 常见问题

| 现象 | 原因 |
| --- | --- |
| 报 502 | Windows 系统代理接管了发往 127.0.0.1 的请求，通常说明 Qdrant 容器没在运行，先 `docker ps` |
| `/retrieve` 返回 503 | Qdrant 没启动或连不上 |
| 数字人说知识库服务没有启动 | uvicorn 没在运行，或设置里的检索服务地址不对 |
| 改了资料但回答没变 | 重新运行 `backend.ingest`；Unity 不用重启 |
| 资料里没有的问题也检索到了片段 | 阈值挡不住语义相近的无关资料，提示词会要求模型说明知识库里没有 |
