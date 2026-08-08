#!/usr/bin/env python3
import json, os, sys, urllib.request, urllib.parse, difflib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
MANIFEST=ROOT/'.tactix'/'ai-bridge.json'
MODEL=os.environ.get('TACTIX_OLLAMA_MODEL','qwen2.5-coder:3b')
OLLAMA=os.environ.get('OLLAMA_HOST','http://127.0.0.1:11434').rstrip('/')
MAX_FILES=6; MAX_BYTES=120000

def bridge(method,path,payload=None):
 m=json.loads(MANIFEST.read_text()); data=None if payload is None else json.dumps(payload).encode()
 req=urllib.request.Request(m['url'].rstrip('/')+path,data=data,method=method,headers={'Authorization':'Bearer '+m['token'],'Content-Type':'application/json'})
 with urllib.request.urlopen(req,timeout=20) as r:return json.load(r)
def read_file(path):return bridge('GET','/project/read?path='+urllib.parse.quote(path))['content']
def write_file(path,content):return bridge('POST','/project/write',{'path':path,'content':content})
def ollama(messages):
 body=json.dumps({'model':MODEL,'messages':messages,'stream':False,'format':'json','options':{'temperature':0.1}}).encode()
 req=urllib.request.Request(OLLAMA+'/api/chat',data=body,headers={'Content-Type':'application/json'})
 with urllib.request.urlopen(req,timeout=300) as r:return json.load(r)['message']['content']
SYSTEM='You are the local coding agent for the TACTIX engine. Make small targeted changes only. Preserve architecture and unrelated code. Return ONLY JSON: {"summary":"...","changes":[{"path":"relative/path","content":"complete replacement file"}]}. Maximum 6 changed files.'
def main():
 if not MANIFEST.exists():sys.exit('TACTIX is not running: .tactix/ai-bridge.json not found.')
 task=' '.join(sys.argv[1:]).strip() or input('TACTIX task: ').strip(); print('Model:',MODEL)
 paths=[]
 while len(paths)<MAX_FILES:
  s=input('File to include (blank when done): ').strip()
  if not s:break
  paths.append(s)
 if not paths:sys.exit('No files selected. Give the agent specific files relevant to the task.')
 bundle=[]; originals={}; total=0
 for path in paths:
  c=read_file(path); originals[path]=c; total+=len(c.encode())
  if total>MAX_BYTES:sys.exit('Selected context is too large; use fewer/smaller files.')
  bundle.append('FILE: '+path+'\n```\n'+c+'\n```')
 prompt='TASK:\n'+task+'\n\nPROJECT CONTEXT:\n'+json.dumps(bridge('GET','/context'),indent=2)+'\n\nFILES:\n'+'\n\n'.join(bundle)+'\n\nRECENT CONSOLE:\n'+bridge('GET','/console').get('content','')[-6000:]
 raw=ollama([{'role':'system','content':SYSTEM},{'role':'user','content':prompt}])
 try:result=json.loads(raw)
 except Exception:sys.exit('Model returned invalid JSON:\n'+raw)
 changes=result.get('changes',[])[:MAX_FILES]; print('\n'+result.get('summary','Proposed changes'))
 for ch in changes:
  path=ch.get('path',''); new=ch.get('content',''); old=originals.get(path,'')
  print('\n--- DIFF '+path+' ---'); print(''.join(difflib.unified_diff(old.splitlines(True),new.splitlines(True),fromfile=path,tofile=path+' (proposed)'))[:12000])
 if not changes:return
 if input('\nApply these changes through TACTIX AI Bridge? [y/N] ').strip().lower()!='y':print('Cancelled; nothing written.');return
 for ch in changes:write_file(ch['path'],ch.get('content',''))
 print('Applied',len(changes),'file(s). Existing files were backed up by TACTIX.')
if __name__=='__main__':main()
