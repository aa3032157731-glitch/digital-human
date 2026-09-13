from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
import json,time
class Handler(BaseHTTPRequestHandler):
 def log_message(self,*args): pass
 def do_POST(self):
  body=json.loads(self.rfile.read(int(self.headers.get('Content-Length',0))))
  if body.get('model')=='unauthorized':
   self.send_response(401);self.end_headers();self.wfile.write(b'{"error":"test"}');return
  self.send_response(200)
  streamed=body.get('stream') and body.get('model')!='json'
  self.send_header('Content-Type','text/event-stream' if streamed else 'application/json');self.end_headers()
  try:
   if streamed:
    chunks=['FIRST','SECOND'] if body.get('model')=='slow' else ['hello',' world']
    for item in chunks:
     self.wfile.write(('data: '+json.dumps({'choices':[{'delta':{'content':item}}]})+'\n\n').encode());self.wfile.flush()
     if body.get('model')=='slow':time.sleep(3)
    self.wfile.write(b'data: [DONE]\n\n')
   else:self.wfile.write(json.dumps({'choices':[{'message':{'content':'hello world'},'finish_reason':'stop'}]}).encode())
  except (BrokenPipeError,ConnectionResetError,ConnectionAbortedError): pass
ThreadingHTTPServer(('127.0.0.1',18769),Handler).serve_forever()
