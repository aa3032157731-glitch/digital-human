from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
import json,time
class Handler(BaseHTTPRequestHandler):
 def log_message(self,*args): pass
 def do_POST(self):
  body=json.loads(self.rfile.read(int(self.headers.get('Content-Length',0))))
  if self.path.rstrip('/').endswith('/retrieve'):
   hit={'chunk_id':'mock','source':'campus/library.md','title':'星河校园图书馆','section':'开放时间','record_key':'md:0','text':'图书馆每天 22:00 关门。','score':0.66}
   data=json.dumps({'question':body.get('question',''),'hits':[hit]},ensure_ascii=False).encode('utf-8')
   self.send_response(200);self.send_header('Content-Type','application/json; charset=utf-8');self.end_headers();self.wfile.write(data);return
  if body.get('model')=='unauthorized':
   self.send_response(401);self.end_headers();self.wfile.write(b'{"error":"test"}');return
  self.send_response(200)
  streamed=body.get('stream') and body.get('model')!='json'
  self.send_header('Content-Type','text/event-stream' if streamed else 'application/json');self.end_headers()
  try:
   if streamed:
    chunks=['FIRST','SECOND'] if body.get('model')=='slow' else ['hello',' world']
    if body.get('model')=='rag-echo':
     system=''.join(m.get('content') or '' for m in body.get('messages',[]) if m.get('role')=='system')
     chunks=['rag:','found' if '[资料1]' in system and '22:00' in system else 'down' if '检索服务暂时不可用' in system else 'none']
    for item in chunks:
     self.wfile.write(('data: '+json.dumps({'choices':[{'delta':{'content':item}}]})+'\n\n').encode());self.wfile.flush()
     if body.get('model')=='slow':time.sleep(3)
    self.wfile.write(b'data: [DONE]\n\n')
   else:self.wfile.write(json.dumps({'choices':[{'message':{'content':'hello world'},'finish_reason':'stop'}]}).encode())
  except (BrokenPipeError,ConnectionResetError,ConnectionAbortedError): pass
ThreadingHTTPServer(('127.0.0.1',18769),Handler).serve_forever()
